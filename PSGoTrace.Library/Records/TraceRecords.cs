using System.Collections.Generic;
using System.IO;
using System.Linq;
using PSGoTrace.Library.Records;

namespace TraceViewer.Trace.Records
{
    public readonly struct TraceRecords
    {
        private TraceRecords(IReadOnlyList<TraceEvent> events, IReadOnlyDictionary<ulong, List<TraceFrame>> stacks)
        {
            Events = events;
            Stacks = stacks;
        }

        public IReadOnlyList<TraceEvent> Events { get; }
        public IReadOnlyDictionary<ulong, List<TraceFrame>> Stacks { get; }

        public static TraceRecords FromStream(Stream source, bool leaveOpen = false)
        {
            try
            {
                var rawTrace = RawTrace.Load(source, leaveOpen);
                var rawEvents = EventParser.Process(rawTrace);
                ProcessRawEvents(rawEvents);
                return new TraceRecords(rawEvents.Events, rawEvents.Stacks);
            }
            catch (EndOfStreamException)
            {
                throw new InvalidTraceException("input data truncated");
            }
        }

        private static void ProcessRawEvents(EventParser.Result raw)
        {
            static void AssertRunning(in PDesc p, in GDesc g, TraceEvent ev, bool allowG0)
            {
                var name = EventDescription.Of(ev.Type).Name;
                if (g.State != GStatus.Running)
                    throw new InvalidEventException(ev, $"is not running while {name}");
                if (p.G != ev.G)
                    throw new InvalidEventException(ev, $"is not running g {ev.G} while {name}");
                if (!allowG0 && ev.G == 0)
                    throw new InvalidEventException(ev, $"g 0 did {name}");
            }

            var gs = new Dictionary<ulong, GDesc>();
            var ps = new Dictionary<int, PDesc>();
            var tasks = new Dictionary<ulong, TraceEvent>();
            var activeRegions = new Dictionary<ulong, List<TraceEvent>>();
            gs[0] = new GDesc(GStatus.Running);
            TraceEvent? evGc = null, evStw = null;

            foreach (var ev in raw.Events)
            {
                var g = gs.GetValueOrDefault(ev.G);
                var p = ps.GetValueOrDefault(ev.P);
                var version = raw.Version;

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

            AttachStacks(raw.Events, raw.Stacks);
        }

        private static void AttachStacks(IEnumerable<TraceEvent> events, IDictionary<ulong, List<TraceFrame>> stacks)
        {
            foreach (var e in events.Where(e => e.StackId != 0))
                e.Stack = stacks[e.StackId];
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
    }
}