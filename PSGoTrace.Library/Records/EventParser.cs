using System.Collections.Generic;
using PSGoTrace.Library.Records;

namespace TraceViewer.Trace.Records
{
    internal static class EventParser
    {
        public static Result Process(RawTrace raw)
        {
            long ticksPerSec = 0L, lastSeq = 0L, lastTs = 0L;
            var lastG = 0UL;
            var lastP = 0;
            var timerGoids = new HashSet<ulong>();
            var lastGs = new Dictionary<int, ulong>();
            var batches = new Dictionary<int, List<TraceEvent>>();
            var stacks = new Dictionary<ulong, List<TraceFrame>>();

            int NumberOfArgs(RawEvent rawEvent, EventDescription desc)
            {
                var count = desc.Stack ? desc.Args.Length + 1 : desc.Args.Length;
                switch (rawEvent.Type)
                {
                    case EventType.Stack:
                        return rawEvent.Args.Length;
                    case EventType.Batch:
                    case EventType.Frequency:
                    case EventType.TimerGoroutine:
                        return raw.Version < 1007 ? count + 1 : count;
                }

                count++; // timestamp
                if (raw.Version < 1007) count++; // sequence

                switch (rawEvent.Type)
                {
                    case EventType.GcSweepDone:
                        return raw.Version < 1009 ? count - 2 : count;
                    case EventType.GcStart:
                    case EventType.GoStart:
                    case EventType.GoUnblock:
                        return raw.Version < 1007 ? count - 1 : count;
                    case EventType.GcStwStart:
                        return raw.Version < 1010 ? count - 1 : count;
                    default:
                        return count;
                }
            }

            foreach (var ev in raw.Events)
            {
                var desc = EventDescription.Of(ev.Type);
                var argCount = NumberOfArgs(ev, desc);
                if (ev.Args.Length != argCount)
                    throw new InvalidTraceException(
                        $"${desc.Name} has wrong number of args at {ev.Offset}: want {argCount}, got {ev.Args.Length}");
                switch (ev.Type)
                {
                    case EventType.Batch:
                        lastGs[lastP] = lastG;
                        lastP = (int) ev.Args[0];
                        lastG = lastGs.GetValueOrDefault(lastP, 0UL);
                        if (raw.Version < 1007)
                        {
                            lastSeq = (long) ev.Args[1];
                            lastTs = (long) ev.Args[2];
                        }
                        else
                        {
                            lastTs = (long) ev.Args[1];
                        }

                        break;
                    case EventType.Frequency:
                        ticksPerSec = (long) ev.Args[0];
                        if (ticksPerSec <= 0) throw new TimeOrderException();
                        break;
                    case EventType.TimerGoroutine:
                        timerGoids.Add(ev.Args[0]);
                        break;
                    case EventType.Stack:
                        if (ev.Args.Length < 2)
                            throw new InvalidTraceException("Stack event should have at least 2 arguments");
                        var size = ev.Args[1];
                        if (size > 1000)
                            throw new InvalidTraceException($"Stack event bad number of frames {size}");
                        var want = raw.Version < 1007 ? 4 + 4 * size : 2 + 4 * size;
                        if (ev.Args.Length != (int) want)
                            throw new InvalidTraceException(
                                $"Stack event has wrong number of arguments want {want} got {ev.Args.Length}");
                        var id = ev.Args[0];
                        if (id != 0 && size > 0)
                        {
                            var stack = new List<TraceFrame>((int) size);
                            for (var i = 0ul; i < size; i++)
                            {
                                var start = 2 + i * 4;
                                stack.Add(raw.Version < 1007
                                    ? new TraceFrame(ev.Args[2 + i])
                                    : new TraceFrame(ev.Args[start], raw.Strings[ev.Args[start + 1]],
                                        raw.Strings[ev.Args[start + 2]], (int) ev.Args[start + 3]));
                            }

                            stacks[id] = stack;
                        }

                        break;
                    default:
                        var e = new TraceEvent(ev.Offset, ev.Type, lastP, lastG);
                        int argOffset;
                        if (raw.Version < 1007)
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
                                    e.StringArgs = new[] {raw.Strings[e.Args[2]]};
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
                                e.StringArgs = new[] {raw.Strings[e.Args[2]]};
                                break;
                            case EventType.UserRegion:
                                // e.Args 0: taskID, 1: mode, 2:nameID
                                e.StringArgs = new[] {raw.Strings[e.Args[2]]};
                                break;
                            case EventType.UserLog:
                                // e.Args 0: taskID, 1:keyID, 2: stackID
                                e.StringArgs = new[] {raw.Strings[e.Args[1]], ev.StringArgs![0]};
                                break;
                        }

                        batches.GetOrAdd(lastP, _ => new List<TraceEvent>()).Add(e);
                        break;
                }
            }

            if (batches.Count == 0) throw new EmptyTraceException();
            if (ticksPerSec == 0) throw new InvalidTraceException("no EvFrequency event");
            var events = new EventsOrder(raw.Version).SortEvents(batches);

            // Translate cpu ticks to real time.
            var minTs = events[0].Ts;
            var freq = 1e9 / ticksPerSec;
            foreach (var ev in events)
            {
                ev.Ts = (long) (freq * (ev.Ts - minTs));
                if (timerGoids.Contains(ev.G) && ev.Type == EventType.GoUnblock)
                    ev.P = (int) ProcIdentifier.Timer;
                if (ev.Type == EventType.GoSysExit)
                    ev.P = (int) ProcIdentifier.Syscall;
            }

            RemoveFutile(events);

            return new Result(events, stacks, raw.Version);
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
                            g.wakeup?.ForEach(wakeup => futile.Add(wakeup));
                        }

                        gs.Remove(ev.G);
                        break;
                }
            }

            // Phase 2: remove futile wakeup sequences.
            events.RemoveAll(e => futile.Contains(e));
        }

        public readonly struct Result
        {
            public Result(List<TraceEvent> events, Dictionary<ulong, List<TraceFrame>> stacks, int version)
            {
                Events = events;
                Stacks = stacks;
                Version = version;
            }

            public List<TraceEvent> Events { get; }
            public Dictionary<ulong, List<TraceFrame>> Stacks { get; }

            public int Version { get; }
        }
    }
}