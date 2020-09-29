using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PSGoTrace.Library.Parser
{
    public class TraceParser : IDisposable
    {
        private static readonly byte[] FileHeader = Encoding.UTF8.GetBytes(" trace\x00\x00\x00\x00");
        private static readonly int[] SupportedVersions = {1005, 1007, 1008, 1009, 1010, 1011};
        private readonly BinaryReader _reader;
        private readonly IProgressRegistry? _registry;
        private readonly IProgressRegistry.IHandle? _total;

        public TraceParser(Stream source, IProgressRegistry? registry = null, bool leaveOpen = false)
        {
            _reader = new BinaryReader(source, Encoding.UTF8, leaveOpen);
            _total = registry?.Start("Parse trace data", "read the binary ");
            _registry = registry?.Fork(_total!);
        }

        public TraceParser(string path, IProgressRegistry? registry = null) : this(File.OpenRead(path), registry, false)
        {
        }

        public void Dispose()
        {
            _reader.Dispose();
        }

        public IList<TraceEvent> Parse()
        {
            var content = ParseContent();
            var sorter = new EventsSorter(content.Version);
            var events = sorter.SortEvents(content.Timeline);

            TranslateTimestamp(events, content);
            RemoveFutile(events);
            LinkEvents(events, content.Version);
            AttachStacks(events, content);

            return events;
        }

        private static void TranslateTimestamp(List<TraceEvent> events, TraceContent content)
        {
            var minTs = events[0].Ts;
            var freq = 1e9 / content.Frequency;
            foreach (var ev in events)
            {
                ev.Ts = (long) (freq * (ev.Ts - minTs));
                if (content.Timers.Contains(ev.G) && ev.Type == EventType.GoUnblock)
                    ev.P = (int) ProcIdentifier.Timer;
                if (ev.Type == EventType.GoSysExit)
                    ev.P = (int) ProcIdentifier.Syscall;
            }
        }

        private TraceContent ParseContent()
        {
            var events = new List<RawEvent>();
            var strings = new Dictionary<ulong, string>();
            var stackInfo = new Dictionary<ulong, RawEvent>();
            var version = ParseHeader();
            var frequency = 0L;
            var timers = new HashSet<ulong>();
            using var progress = _registry?.Start("Load trace", "Read and parse the binary trace data");

            var end = _reader.BaseStream.Length;
            while (Position < end)
            {
                if (!(progress is null)) progress.PercentComplete = Position / (float) end;
                var eventStartPosition = Position;
                var eventTypeAndArgsCount = _reader.ReadByte();
                var type = (EventType) (eventTypeAndArgsCount & ~(0b11 << 6));
                var argsCount = (eventTypeAndArgsCount >> 6) + 1;
                var inlineArgs = (byte) 4;
                if (version < 1007)
                {
                    argsCount++;
                    inlineArgs++;
                }

                if (type == EventType.None || EventDescription.Of(type).MinVersion > version)
                    throw new InvalidTraceException("unknown type");

                if (type == EventType.String)
                {
                    // String dictionary entry [ID, length, string].
                    var id = ReadVal();
                    if (id == 0)
                        throw new InvalidTraceException($"{Position} has invalid id 0");
                    if (strings.ContainsKey(id))
                        throw new InvalidTraceException($"{Position} has duplicate id {id}");
                    var value = ReadStr();
                    if (value.Length == 0)
                        throw new InvalidTraceException($"{Position} has invalid length 0");
                    strings[id] = value;
                    continue;
                }

                var ev = new RawEvent(type, (int) eventStartPosition);
                if (argsCount < inlineArgs)
                {
                    ev.Args = new ulong[argsCount];
                    for (var i = 0; i < argsCount; i++)
                        ev.Args[i] = ReadVal();
                }
                else
                {
                    var evLength = ReadVal();
                    var start = _reader.BaseStream.Position;
                    var buffer = new List<ulong>();
                    while (evLength > (ulong) (_reader.BaseStream.Position - start))
                    {
                        var arg = ReadVal();
                        buffer.Add(arg);
                    }

                    if (evLength != (ulong) (Position - start))
                        throw new InvalidTraceException(
                            $"event has wrong length at {Position}, want: {evLength}, got: {Position - start}");
                    ev.Args = buffer.ToArray();
                }

                if (ev.Type == EventType.UserLog) ev.StringArgs = new[] {ReadStr()};

                switch (ev.Type)
                {
                    case EventType.Frequency:
                        frequency = (long) ev.Args[0];
                        if (frequency <= 0) throw new TimeOrderException();
                        break;
                    case EventType.TimerGoroutine:
                        timers.Add(ev.Args[0]);
                        break;
                    case EventType.Stack:
                        if (ev.Args.Length < 2)
                            throw new InvalidTraceException("Stack event should have at least 2 arguments");
                        var size = ev.Args[1];
                        if (size > 1000)
                            throw new InvalidTraceException($"Stack event bad number of frames {size}");
                        var want = version < 1007 ? 4 + 4 * size : 2 + 4 * size;
                        if (ev.Args.Length != (int) want)
                            throw new InvalidTraceException(
                                $"Stack event has wrong number of arguments want {want} got {ev.Args.Length}");

                        stackInfo[ev.Args[0]] = ev;
                        break;
                    default:
                        events.Add(ev);
                        break;
                }
            }

            return new TraceContent(events, strings, version, frequency, timers, stackInfo);
        }

        private static void RemoveFutile(List<TraceEvent> events)
        {
            // Two non-trivial aspects:
            // 1. A goroutine can be preempted during a futile wakeup and migrate to another P.
            //	We want to remove all of that.
            // 2. Tracing can start in the middle of a futile wakeup.
            //	That is, we can see a futile wakeup event w/o the actual wakeup before it.
            // postProcessTrace runs after us and ensures that we leave the trace in a consistent state.

            // Phase 1: determine futile wakeup sequences.
            var gs = new Dictionary<ulong, (bool futile, List<TraceEvent> wakeup)>();
            var futile = new HashSet<TraceEvent>();
            foreach (var ev in events)
            {
                (bool futile, List<TraceEvent> wakeup) g;
                switch (ev.Type)
                {
                    case EventType.GoUnblock:
                        g = gs.GetValueOrDefault(ev.Args[0]);
                        g.wakeup = new List<TraceEvent> {ev};
                        gs[ev.Args[0]] = g;
                        break;
                    case EventType.GoStart:
                    case EventType.GoPreempt:
                    case EventType.FutileWakeup:
                        g = gs.GetValueOrDefault(ev.G, (false, new List<TraceEvent>()));
                        g.wakeup.Add(ev);
                        g.futile = ev.Type == EventType.FutileWakeup;
                        gs[ev.G] = g;
                        break;
                    case EventType.GoBlock:
                    case EventType.GoBlockRecv:
                    case EventType.GoBlockSelect:
                    case EventType.GoBlockSync:
                    case EventType.GoBlockCond:
                        g = gs.GetValueOrDefault(ev.G);
                        if (g.futile)
                        {
                            futile.Add(ev);
                            g.wakeup.ForEach(wakeup => futile.Add(wakeup));
                        }

                        gs.Remove(ev.G);
                        break;
                }
            }

            // Phase 2: remove futile wakeup sequences.
            events.RemoveAll(e => futile.Contains(e));
        }

        private static void LinkEvents(IList<TraceEvent> events, int version)
        {
            var gs = new Dictionary<ulong, GDesc>();
            var ps = new Dictionary<int, PDesc>();
            var tasks = new Dictionary<ulong, TraceEvent>();
            var activeRegions = new Dictionary<ulong, List<TraceEvent>>();
            gs[0] = new GDesc(GStatus.Running);
            TraceEvent? evGc = null, evStw = null;

            foreach (var ev in events)
            {
                var g = gs.GetValueOrDefault(ev.G);
                var p = ps.GetValueOrDefault(ev.P);

                switch (ev.Type)
                {
                    case EventType.ProcStart:
                    {
                        if (p.Running) throw new InvalidEventException(ev, "is running before start");
                        p.Running = true;
                        break;
                    }
                    case EventType.ProcStop:
                    {
                        if (!p.Running) throw new InvalidEventException(ev, "is not running before stop");
                        if (p.G != 0) throw new InvalidEventException(ev, "is running during stop");
                        p.Running = false;
                        break;
                    }
                    case EventType.GcStart:
                    {
                        if (evGc != null) throw new InvalidEventException(ev, "prev GC is not ended before a new one");
                        evGc = ev;
                        ev.P = (int) ProcIdentifier.Gc;
                        break;
                    }
                    case EventType.GcDone:
                    {
                        if (evGc == null) throw new InvalidEventException(ev, "bogus GC end");
                        evGc.Link = ev;
                        evGc = null;
                        break;
                    }
                    case EventType.GcStwStart:
                    {
                        if (version < 1010)
                        {
                            if (p.EvStw != null)
                                throw new InvalidEventException(ev, "previous STW is not ended before a new one");
                            p.EvStw = ev;
                        }
                        else
                        {
                            if (evStw != null)
                                throw new InvalidEventException(ev, "previous STW is not ended before a new one");
                            evStw = ev;
                        }

                        break;
                    }
                    case EventType.GcStwDone:
                    {
                        if (version < 1010)
                        {
                            if (p.EvStw == null) throw new InvalidEventException(ev, "bogus STW end");
                            p.EvStw.Link = ev;
                            p.EvStw = null;
                        }
                        else
                        {
                            if (evStw == null) throw new InvalidEventException(ev, "bogus STW end");
                            evStw.Link = ev;
                            evStw = null;
                        }

                        break;
                    }
                    case EventType.GcSweepStart:
                    {
                        if (p.EvSweep != null)
                            throw new InvalidEventException(ev, "previous sweeping is not ended before a new one");
                        p.EvSweep = ev;
                        break;
                    }
                    case EventType.GcSweepDone:
                    {
                        if (p.EvSweep == null) throw new InvalidEventException(ev, "bogus sweeping end");
                        p.EvSweep.Link = ev;
                        p.EvSweep = null;
                        break;
                    }
                    case EventType.GcMarkAssistStart:
                    {
                        if (g.EvMarkAssist != null)
                            throw new InvalidEventException(ev, "previous mark assist is not ended before a new one");
                        g.EvMarkAssist = ev;
                        break;
                    }
                    case EventType.GcMarkAssistDone:
                    {
                        // Unlike most events, mark assists can be in progress when a
                        // goroutine starts tracing, so we can't report an error here.
                        if (g.EvMarkAssist != null) g.EvMarkAssist.Link = ev;
                        g.EvMarkAssist = null;
                        break;
                    }
                    case EventType.GoWaiting:
                    {
                        if (g.State != GStatus.Runnable)
                            throw new InvalidEventException(ev, "is not runnable before EvGoWaiting");
                        g.State = GStatus.Waiting;
                        g.Ev = ev;
                        break;
                    }
                    case EventType.GoInSyscall:
                    {
                        if (g.State != GStatus.Runnable)
                            throw new InvalidEventException(ev, "is not runnable before EvGoInSyscall");
                        g.State = GStatus.Waiting;
                        g.Ev = ev;
                        break;
                    }
                    case EventType.GoCreate:
                    {
                        AssertRunning(p, g, ev, true);
                        if (gs.ContainsKey(ev.Args[0]))
                            throw new InvalidEventException(ev, "already exists");
                        gs[ev.Args[0]] = new GDesc(GStatus.Runnable, ev, ev);
                        break;
                    }
                    case EventType.GoStart:
                    case EventType.GoStartLabel:
                    {
                        if (g.State != GStatus.Runnable)
                            throw new InvalidEventException(ev, "is not runnable before start");
                        if (p.G != 0) throw new InvalidEventException(ev, $"is already running g {p.G}");
                        g.State = GStatus.Running;
                        g.EvStart = ev;
                        p.G = ev.G;
                        var create = g.EvCreate?.Args[1];
                        if (create != null)
                        {
                            if (version < 1007) ev.Stack = new List<TraceFrame> {new TraceFrame(create.Value + 1)};
                            else ev.StackId = create.Value;
                            g.EvCreate = null;
                        }

                        if (g.Ev != null)
                        {
                            g.Ev.Link = ev;
                            g.Ev = null;
                        }

                        break;
                    }
                    case EventType.GoEnd:
                    case EventType.GoStop:
                    {
                        AssertRunning(p, g, ev, false);
                        g.EvStart!.Link = ev;
                        g.EvStart = null;
                        g.State = GStatus.Dead;
                        p.G = 0;

                        if (ev.Type == EventType.GoEnd && activeRegions.TryGetValue(ev.G, out var regions))
                        {
                            regions.ForEach(s => s.Link = ev);
                            activeRegions.Remove(ev.G);
                        }

                        break;
                    }
                    case EventType.GoSched:
                    case EventType.GoPreempt:
                    {
                        AssertRunning(p, g, ev, false);
                        g.State = GStatus.Runnable;
                        g.EvStart!.Link = ev;
                        g.EvStart = null;
                        p.G = 0;
                        g.Ev = ev;
                        break;
                    }
                    case EventType.GoUnblock:
                    {
                        if (g.State != GStatus.Running)
                            throw new InvalidEventException(ev, "is not running while unpark");
                        if (ev.P != (int) ProcIdentifier.Timer && p.G != ev.G)
                            throw new InvalidEventException(ev, "is not running g while unpark");
                        var g1 = gs[ev.Args[0]];
                        if (g1.State != GStatus.Waiting)
                            throw new InvalidEventException(ev, $"g {ev.Args[0]} is not waiting before unpark");
                        if (g1.Ev?.Type == EventType.GoBlockNet && ev.P != (int) ProcIdentifier.Timer)
                            ev.P = (int) ProcIdentifier.NetPoll;
                        if (g1.Ev != null) g1.Ev.Link = ev;
                        g1.State = GStatus.Runnable;
                        g1.Ev = ev;
                        gs[ev.Args[0]] = g1;
                        break;
                    }
                    case EventType.GoSysCall:
                    {
                        AssertRunning(p, g, ev, false);
                        g.Ev = ev;
                        break;
                    }
                    case EventType.GoSysBlock:
                    {
                        AssertRunning(p, g, ev, false);
                        g.State = GStatus.Waiting;
                        g.EvStart!.Link = ev;
                        g.EvStart = null;
                        p.G = 0;
                        break;
                    }
                    case EventType.GoSysExit:
                    {
                        if (g.State != GStatus.Waiting)
                            throw new InvalidEventException(ev, "is not waiting during syscall exit");
                        if (g.Ev?.Type == EventType.GoSysCall) g.Ev.Link = ev;
                        g.State = GStatus.Runnable;
                        g.Ev = ev;
                        break;
                    }
                    case EventType.GoSleep:
                    case EventType.GoBlock:
                    case EventType.GoBlockSend:
                    case EventType.GoBlockRecv:
                    case EventType.GoBlockSelect:
                    case EventType.GoBlockSync:
                    case EventType.GoBlockCond:
                    case EventType.GoBlockNet:
                    case EventType.GoBlockGc:
                    {
                        AssertRunning(p, g, ev, false);
                        g.State = GStatus.Waiting;
                        g.Ev = ev;
                        g.EvStart!.Link = ev;
                        g.EvStart = null;
                        p.G = 0;
                        break;
                    }
                    case EventType.UserTaskCreate:
                    {
                        if (tasks.ContainsKey(ev.Args[0]))
                            throw new InvalidEventException(ev, $"task id {ev.Args[0]} conflicts");
                        tasks[ev.Args[0]] = ev;
                        break;
                    }
                    case EventType.UserTaskEnd:
                    {
                        if (tasks.TryGetValue(ev.Args[0], out var createEv))
                        {
                            createEv.Link = ev;
                            tasks.Remove(ev.Args[0]);
                        }

                        break;
                    }
                    case EventType.UserRegion:
                    {
                        var mode = ev.Args[1];
                        if (mode == 0)
                        {
                            activeRegions.GetOrAdd(ev.G, _ => new List<TraceEvent>()).Add(ev);
                        }
                        else if (mode == 1)
                        {
                            if (activeRegions.TryGetValue(ev.G, out var regions))
                            {
                                var s = regions[^1];
                                if (s.Args[0] != ev.Args[0] || s.StringArgs![0] != ev.StringArgs![0])
                                    throw new InvalidTraceException(
                                        "misuse of region: span end when the inner-most active span start");
                                s.Link = ev;
                                if (regions.Count > 1) regions.RemoveAt(regions.Count - 1);
                                else activeRegions.Remove(ev.G);
                            }
                        }
                        else
                        {
                            throw new InvalidTraceException($"invalid user region mode: {mode}");
                        }

                        break;
                    }
                }

                gs[ev.G] = g;
                ps[ev.P] = p;
            }
        }

        private static void AssertRunning(in PDesc p, in GDesc g, TraceEvent ev, bool allowG0)
        {
            var name = EventDescription.Of(ev.Type).Name;
            if (g.State != GStatus.Running)
                throw new InvalidEventException(ev, $"is not running while {name}");
            if (p.G != ev.G)
                throw new InvalidEventException(ev, $"is not running g {ev.G} while {name}");
            if (!allowG0 && ev.G == 0)
                throw new InvalidEventException(ev, $"g 0 did {name}");
        }

        private static void AttachStacks(IEnumerable<TraceEvent> events, TraceContent content)
        {
            var stacks = new Dictionary<ulong, List<TraceFrame>>();
            foreach (var e in events)
            {
                if (e.StackId == 0) continue;
                if (!stacks.TryGetValue(e.StackId, out var stack))
                {
                    var info = content.StackInfo[e.StackId];
                    var id = info.Args[0];
                    var size = info.Args[1];
                    if (id != 0 && size > 0)
                    {
                        stack = new List<TraceFrame>((int) size);
                        for (var i = 0ul; i < size; i++)
                        {
                            var start = 2 + i * 4;
                            stack.Add(content.Version < 1007
                                ? new TraceFrame(info.Args[2 + i])
                                : new TraceFrame(info.Args[start], content.Strings[info.Args[start + 1]],
                                    content.Strings[info.Args[start + 2]], (int) info.Args[start + 3]));
                        }

                        stacks[id] = stack;
                    }
                }

                if (!(stack is null)) e.Stack = stack;
            }
        }

        private long Position => _reader.BaseStream.Position;

        private string ReadStr()
        {
            var size = ReadVal();
            if (size == 0)
                return "";
            if (size > 1e6)
                throw new InvalidTraceException($"string at {Position} has incorrect size");
            return Encoding.UTF8.GetString(_reader.ReadBytes((int) size));
        }

        private ulong ReadVal()
        {
            var value = 0ul;
            for (var i = 0; i < 10; i++)
            {
                var data = _reader.ReadByte();
                value |= (ulong) (data & 0x7f) << (i * 7);
                if ((data & 0x80) == 0) return value;
            }

            throw new InvalidTraceException($"bad value at offset {Position}");
        }

        private int ParseHeader()
        {
            var buf = _reader.ReadBytes(16);
            if (buf[0] != 'g' || buf[1] != 'o' || buf[2] != ' ' ||
                buf[3] < '1' || buf[3] > '9' ||
                buf[4] != '.' ||
                buf[5] < '1' || buf[5] > '9')
                throw new InvalidTraceException("bad trace header");

            var version = buf[5] - '0';
            var i = 0;
            for (; buf[6 + i] >= '0' && buf[6 + i] <= '9' && i < 2; i++)
                version = version * 10 + (buf[6 + i] - '0');
            version += (buf[3] - '0') * 1000;

            if (!buf[(6 + i)..].SequenceEqual(FileHeader[..(10 - i)]))
                throw new InvalidTraceException("bad trace header");

            if (!SupportedVersions.Contains(version))
                throw new InvalidTraceException(
                    $"unsupported trace file version {version / 1000}.{version % 1000}");

            return version;
        }

        private readonly struct TraceContent
        {
            public TraceContent(List<RawEvent> events, Dictionary<ulong, string> strings,
                int version, long frequency, HashSet<ulong> timers, Dictionary<ulong, RawEvent> stackInfo)
            {
                if (frequency == 0) throw new InvalidTraceException("no EvFrequency event");
                Strings = strings;
                Version = version;
                Frequency = frequency;
                Timers = timers;
                StackInfo = stackInfo;
                Timeline = new Dictionary<int, List<TraceEvent>>();

                var lastGs = new Dictionary<int, ulong>();
                long lastSeq = 0L, lastTs = 0L;
                var lastG = 0UL;
                var lastP = 0;
                foreach (var ev in events)
                {
                    var argCount = ev.NumberOfArgs(version);
                    var desc = EventDescription.Of(ev.Type);
                    if (ev.Args.Length != argCount)
                        throw new InvalidTraceException(
                            $"${desc.Name} has wrong number of args at {ev.Offset}: want {argCount}, got {ev.Args.Length}");
                    if (ev.Type == EventType.Batch)
                    {
                        lastGs[lastP] = lastG;
                        lastP = (int) ev.Args[0];
                        lastG = lastGs.GetValueOrDefault(lastP, 0UL);
                        if (version < 1007)
                        {
                            lastSeq = (long) ev.Args[1];
                            lastTs = (long) ev.Args[2];
                        }
                        else
                        {
                            lastTs = (long) ev.Args[1];
                        }
                    }
                    else
                    {
                        var e = new TraceEvent(ev.Offset, ev.Type, lastP, lastG);
                        int argOffset;
                        if (version < 1007)
                        {
                            e.Seq = lastSeq + (long) ev.Args[0];
                            e.Ts = lastTs + (long) ev.Args[1];
                            lastSeq = e.Seq;
                            argOffset = 2;
                        }
                        else
                        {
                            e.Ts = lastTs + (long) ev.Args[0];
                            argOffset = 1;
                        }

                        lastTs = e.Ts;
                        for (var i = argOffset; i < argCount; i++)
                            if (i == argCount - 1 && desc.Stack) e.StackId = ev.Args[i];
                            else e.Args[i - argOffset] = ev.Args[i];

                        switch (ev.Type)
                        {
                            case EventType.GoStart:
                            case EventType.GoStartLocal:
                            case EventType.GoStartLabel:
                                lastG = e.Args[0];
                                e.G = lastG;
                                if (ev.Type == EventType.GoStartLabel)
                                    e.StringArgs = new[] {Strings[e.Args[2]]};
                                break;
                            case EventType.GcStwStart:
                                e.G = 0;
                                e.StringArgs = e.Args[0] switch
                                {
                                    0 => new[] {"mark termination"},
                                    1 => new[] {"sweep termination"},
                                    var x => throw new InvalidTraceException($"unknown STW kind {x}")
                                };
                                break;
                            case EventType.GcStart:
                            case EventType.GcDone:
                            case EventType.GcStwDone:
                                e.G = 0;
                                break;
                            case EventType.GoEnd:
                            case EventType.GoStop:
                            case EventType.GoSched:
                            case EventType.GoPreempt:
                            case EventType.GoSleep:
                            case EventType.GoBlock:
                            case EventType.GoBlockSend:
                            case EventType.GoBlockRecv:
                            case EventType.GoBlockSelect:
                            case EventType.GoBlockSync:
                            case EventType.GoBlockCond:
                            case EventType.GoBlockNet:
                            case EventType.GoSysBlock:
                            case EventType.GoBlockGc:
                                lastG = 0;
                                break;
                            case EventType.GoSysExit:
                            case EventType.GoWaiting:
                            case EventType.GoInSyscall:
                                e.G = e.Args[0];
                                break;
                            case EventType.UserTaskCreate:
                                // e.Args 0: taskID, 1:parentID, 2:nameID
                                e.StringArgs = new[] {strings[e.Args[2]]};
                                break;
                            case EventType.UserRegion:
                                // e.Args 0: taskID, 1: mode, 2:nameID
                                e.StringArgs = new[] {strings[e.Args[2]]};
                                break;
                            case EventType.UserLog:
                                // e.Args 0: taskID, 1:keyID, 2: stackID
                                e.StringArgs = new[] {strings[e.Args[1]], ev.StringArgs![0]};
                                break;
                        }

                        Timeline.GetOrAdd(lastP, _ => new List<TraceEvent>()).Add(e);
                    }
                }

                if (Timeline.Count == 0) throw new EmptyTraceException();
            }

            public Dictionary<ulong, string> Strings { get; }
            public int Version { get; }
            public long Frequency { get; }
            public HashSet<ulong> Timers { get; }
            public Dictionary<ulong, RawEvent> StackInfo { get; }
            public Dictionary<int, List<TraceEvent>> Timeline { get; }
        }

        private struct PDesc
        {
            public bool Running { get; set; }
            public ulong G { get; set; }
            public TraceEvent? EvStw { get; set; }
            public TraceEvent? EvSweep { get; set; }
        }

        private struct GDesc
        {
            public GDesc(GStatus state, TraceEvent? ev = null, TraceEvent? evCreate = null) : this()
            {
                State = state;
                Ev = ev;
                EvCreate = evCreate;
            }

            public GStatus State { get; set; }
            public TraceEvent? Ev { get; set; }
            public TraceEvent? EvStart { get; set; }
            public TraceEvent? EvCreate { get; set; }
            public TraceEvent? EvMarkAssist { get; set; }
        }

        private struct RawEvent
        {
            public RawEvent(EventType type, int offset)
            {
                Offset = offset;
                Type = type;
                Args = null!;
                StringArgs = null;
            }

            internal int Offset { get; }
            internal EventType Type { get; }
            internal ulong[] Args { get; set; }
            internal string[]? StringArgs { get; set; }

            public int NumberOfArgs(int version)
            {
                var desc = EventDescription.Of(Type);
                var count = desc.Stack ? desc.Args.Length + 1 : desc.Args.Length;
                switch (Type)
                {
                    case EventType.Stack:
                        return Args.Length;
                    case EventType.Batch:
                    case EventType.Frequency:
                    case EventType.TimerGoroutine:
                        return version < 1007 ? count + 1 : count;
                }

                count++; // timestamp
                if (version < 1007) count++; // sequence

                switch (Type)
                {
                    case EventType.GcSweepDone:
                        return version < 1009 ? count - 2 : count;
                    case EventType.GcStart:
                    case EventType.GoStart:
                    case EventType.GoUnblock:
                        return version < 1007 ? count - 1 : count;
                    case EventType.GcStwStart:
                        return version < 1010 ? count - 1 : count;
                    default:
                        return count;
                }
            }
        }
    }
}