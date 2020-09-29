using System;
using System.Collections.Generic;
using System.Linq;
using HPCsharp;
using PSGoTrace.Library.Helper;

namespace PSGoTrace.Library.Parser
{
    internal class EventsSorter
    {
        private const ulong Unordered = ~0UL;
        private const ulong Garbage = ~0UL - 1;
        private const ulong Noseq = ~0UL;
        private const ulong Seqinc = ~0UL - 1;
        private readonly int _version;
        private readonly IProgressRegistry? _registry;

        public EventsSorter(int version, IProgressRegistry? registry = null)
        {
            _version = version;
            _registry = registry;
        }

        public List<TraceEvent> SortEvents(IDictionary<int, List<TraceEvent>> rawBatches)
        {
            return _version < 1007 ? Sort1005(rawBatches) : Sort1007(rawBatches);
        }

        private List<TraceEvent> Sort1007(IDictionary<int, List<TraceEvent>> rawBatches)
        {
            var pending = 0;
            Span<EventBatch> batches = new EventBatch[rawBatches.Count];
            var result = new List<TraceEvent>();
            foreach (var (i, eventList) in rawBatches.Values.Select((val, i) => (i, val)))
            {
                pending += eventList.Count;
                batches[i] = new EventBatch(eventList, false);
            }

            using var totalProgress = _registry?.Start("Sort Events", "Sort events based on states transition");
            var gs = new Dictionary<ulong, GState>();
            var frontier = new FibonacciHeap<OrderEvent>();
            var totalCount = pending;

            using (var progress =
                _registry?.Start("Reorganize events", "Reorganize events based on relationship", totalProgress))
            {
                for (; pending != 0; pending--)
                {
                    if (!(progress is null)) progress.PercentComplete = (totalCount - pending) / (float) totalCount;

                    for (var i = 0; i < rawBatches.Count; i++)
                    {
                        ref var b = ref batches[i];
                        if (b.Selected || !b.HasMore()) continue;
                        var ev = b.Head;
                        var (g, init, next) = StateTransition(ev);
                        if (!TransitionReady(g, gs.GetValueOrDefault(g), init)) continue;
                        frontier.Add(new OrderEvent(ev, i, g, init, next));
                        b.MoveNext();
                        b.Selected = true;
                        switch (ev.Type)
                        {
                            case EventType.GoStartLocal:
                                ev.Type = EventType.GoStart;
                                break;
                            case EventType.GoUnblockLocal:
                                ev.Type = EventType.GoUnblock;
                                break;
                            case EventType.GoSysExitLocal:
                                ev.Type = EventType.GoSysExit;
                                break;
                        }
                    }

                    if (frontier.Count == 0)
                        throw new InvalidTraceException("no consistent ordering of events possible");
                    var f = frontier.Pop();
                    Transition(gs, f.G, f.Init, f.Next);
                    result.Add(f.Event);
                    if (!batches[f.Batch].Selected) throw new Exception("frontier batch is not selected");
                    batches[f.Batch].Selected = false;
                }
            }

            if (!(totalProgress is null))
            {
                totalProgress.CurrentOperation = "Post process events";
                totalProgress.PercentComplete = 80;
            }

            // At this point we have a consistent stream of events.
            // Make sure time stamps respect the ordering.
            // The tests will skip (not fail) the test case if they see this error.
            for (var i = 0; i < result.Count - 1; i++)
                if (TraceEvent.TsComparer.Compare(result[i], result[i + 1]) > 0)
                    throw new TimeOrderException();

            // The last part is giving correct timestamps to EvGoSysExit events.
            // The problem with EvGoSysExit is that actual syscall exit timestamp (ev.Args[2])
            // is potentially acquired long before event emission. So far we've used
            // timestamp of event emission (ev.Ts).
            // We could not set ev.Ts = ev.Args[2] earlier, because it would produce
            // seemingly broken timestamps (misplaced event).
            // We also can't simply update the timestamp and resort events, because
            // if timestamps are broken we will misplace the event and later report
            // logically broken trace (instead of reporting broken timestamps).
            var lastSysBlock = new Dictionary<ulong, long>();
            foreach (var ev in result)
                switch (ev.Type)
                {
                    case EventType.GoSysBlock:
                    case EventType.GoInSyscall:
                        lastSysBlock[ev.G] = ev.Ts;
                        break;
                    case EventType.GoSysExit:
                    {
                        var ts = (long) ev.Args[2];
                        if (ts == 0) continue;
                        var block = lastSysBlock[ev.G];
                        if (block == 0) throw new InvalidTraceException("stray syscall exit");
                        if (ts < block) throw new TimeOrderException();
                        ev.Ts = ts;
                        break;
                    }
                }

            if (!(totalProgress is null))
            {
                totalProgress.CurrentOperation = "Sort events based on timestamp";
                totalProgress.PercentComplete = 85;
            }

            return result.SortMergeStablePar(TraceEvent.TsComparer);
        }

        private static List<TraceEvent> Sort1005(IDictionary<int, List<TraceEvent>> rawBatches)
        {
            var result = new List<TraceEvent>();
            foreach (var (_, batch) in rawBatches) result.AddRange(batch);
            foreach (var ev in result.Where(ev => ev.Type == EventType.GoSysExit))
            {
                // EvGoSysExit emission is delayed until the thread has a P.
                // Give it the real sequence number and time stamp.
                ev.Seq = (long) ev.Args[1];
                if (ev.Args[2] != 0) ev.Ts = (long) ev.Args[2];
            }

            result.Sort(TraceEvent.SeqComparer);
            for (var i = 0; i < result.Count - 1; i++)
                if (TraceEvent.TsComparer.Compare(result[i], result[i + 1]) > 0)
                    throw new TimeOrderException();
            return result;
        }

        private static void Transition(Dictionary<ulong, GState> gs, ulong g, GState init, GState next)
        {
            if (g == Unordered) return;
            var curr = gs.GetValueOrDefault(g);
            if (!TransitionReady(g, curr, init)) throw new Exception("event sequences are broken");
            gs[g] = next.Seq switch
            {
                Noseq => new GState(curr.Seq, next.Status),
                Seqinc => new GState(curr.Seq + 1, next.Status),
                _ => next
            };
        }

        private static bool TransitionReady(ulong g, GState curr, GState init)
        {
            return g == Unordered || (init.Seq == Noseq || init.Seq == curr.Seq) && init.Status == curr.Status;
        }

        private static (ulong g, GState init, GState next) StateTransition(TraceEvent ev)
        {
            switch (ev.Type)
            {
                case EventType.GoCreate:
                    return (ev.Args[0], new GState(0, GStatus.Dead), new GState(1, GStatus.Runnable));
                case EventType.GoWaiting:
                case EventType.GoInSyscall:
                    return (ev.G, new GState(1, GStatus.Runnable), new GState(2, GStatus.Waiting));
                case EventType.GoStart:
                case EventType.GoStartLabel:
                    return (ev.G, new GState(ev.Args[1], GStatus.Runnable),
                        new GState(ev.Args[1] + 1, GStatus.Running));
                case EventType.GoStartLocal:
                    // noseq means that this event is ready for merging as soon as
                    // frontier reaches it (EvGoStartLocal is emitted on the same P
                    // as the corresponding EvGoCreate/EvGoUnblock, and thus the latter
                    // is already merged).
                    // seqinc is a stub for cases when event increments g sequence,
                    // but since we don't know current seq we also don't know next seq.
                    return (ev.G, new GState(Noseq, GStatus.Runnable), new GState(Seqinc, GStatus.Running));
                case EventType.GoBlock:
                case EventType.GoBlockSend:
                case EventType.GoBlockRecv:
                case EventType.GoBlockSelect:
                case EventType.GoBlockSync:
                case EventType.GoBlockCond:
                case EventType.GoBlockNet:
                case EventType.GoSleep:
                case EventType.GoSysBlock:
                case EventType.GoBlockGc:
                    return (ev.G, new GState(Noseq, GStatus.Running), new GState(Noseq, GStatus.Waiting));
                case EventType.GoSched:
                case EventType.GoPreempt:
                    return (ev.G, new GState(Noseq, GStatus.Running), new GState(Noseq, GStatus.Runnable));
                case EventType.GoUnblock:
                case EventType.GoSysExit:
                    return (ev.Args[0], new GState(ev.Args[1], GStatus.Waiting),
                        new GState(ev.Args[1] + 1, GStatus.Runnable));
                case EventType.GoUnblockLocal:
                case EventType.GoSysExitLocal:
                    return (ev.Args[0], new GState(Noseq, GStatus.Waiting), new GState(Seqinc, GStatus.Runnable));
                case EventType.GcStart:
                    return (Garbage, new GState(ev.Args[0], GStatus.Dead), new GState(ev.Args[0] + 1, GStatus.Dead));
                default:
                    return (Unordered, default, default);
            }
        }

        private struct EventBatch
        {
            public EventBatch(List<TraceEvent> events, bool selected)
            {
                _events = events;
                _index = 0;
                Selected = selected;
            }

            private readonly List<TraceEvent> _events;
            private int _index;

            public bool Selected { get; set; }
            public TraceEvent Head => _events[_index];

            public bool HasMore()
            {
                return _index < _events.Count;
            }

            public void MoveNext()
            {
                _index++;
            }
        }

        private readonly struct OrderEvent : IComparable<OrderEvent>
        {
            public int CompareTo(OrderEvent other)
            {
                return Event.Ts.CompareTo(other.Event.Ts);
            }

            public OrderEvent(TraceEvent traceEvent, int batch, ulong g, GState init, GState next)
            {
                Event = traceEvent;
                Batch = batch;
                G = g;
                Init = init;
                Next = next;
            }

            public TraceEvent Event { get; }
            public int Batch { get; }
            public ulong G { get; }
            public GState Init { get; }
            public GState Next { get; }
        }

        private readonly struct GState
        {
            public GState(ulong seq, GStatus status)
            {
                Seq = seq;
                Status = status;
            }

            public ulong Seq { get; }
            public GStatus Status { get; }
        }
    }
}