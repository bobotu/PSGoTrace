using System;
using System.Collections.Generic;
using System.Linq;
using PSGoTrace.Library.Records;
using TraceViewer.Trace.Records;

namespace TraceViewer.Trace.Analyzer
{
    public class GoroutinesAnalyzer
    {
        public GoroutinesAnalyzer(IEnumerable<TraceEvent> events)
        {
            Goroutines = Goroutine.FromEvents(events);
        }

        public IReadOnlyDictionary<ulong, Goroutine> Goroutines { get; }
    }

    public class Goroutine
    {
        private readonly List<UserRegion> _regions = new List<UserRegion>();
        private Desc? _desc = new Desc();

        private ExecutionStat _stat;

        private Goroutine(ulong id, long creationTime)
        {
            Id = id;
            Name = "";
            CreationTime = creationTime;
        }

        public ulong Id { get; }
        public string Name { get; private set; }
        public ulong Pc { get; private set; }
        public long CreationTime { get; }
        public long StartTime { get; private set; }
        public long? EndTime { get; private set; }
        public IReadOnlyList<UserRegion> Regions => _regions;
        public ExecutionStat Stat => _stat;
        private bool Terminated => EndTime.HasValue;

        internal static IReadOnlyDictionary<ulong, Goroutine> FromEvents(IEnumerable<TraceEvent> events)
        {
            var gs = new Dictionary<ulong, Goroutine>();
            long lastTs = 0, gcStartTime = 0;
            foreach (var ev in events)
            {
                lastTs = ev.Ts;
                Goroutine g;
                switch (ev.Type)
                {
                    case EventType.GoCreate:
                        g = new Goroutine(ev.Args[0], ev.Ts);
                        g._desc!.BlockSchedTime = ev.Ts;

                        // When a goroutine is newly created, inherit the
                        // task of the active region. For ease handling of
                        // this case, we create a fake region description with
                        // the task id.
                        if (gs.TryGetValue(ev.G, out var creatorG))
                        {
                            var active = creatorG._desc!.ActiveRegions;
                            if (active.Count > 0)
                            {
                                var s = active[^1];
                                if (s.TaskId != 0)
                                    g._desc.ActiveRegions = new List<UserRegion>
                                        {new UserRegion(s.TaskId, g.Id, start: ev)};
                            }
                        }

                        gs[g.Id] = g;
                        break;
                    case EventType.GoStart:
                    case EventType.GoStartLabel:
                        g = gs[ev.G];
                        if (g.Pc == 0)
                        {
                            g.Pc = ev.Stack[0].Pc;
                            g.Name = ev.Stack[0].Fn;
                        }

                        if (g._desc!.BlockSchedTime != 0)
                        {
                            g._stat.SchedWaitTime += ev.Ts - g._desc.BlockSchedTime;
                            g._desc!.BlockSchedTime = 0;
                        }

                        if (g.StartTime == 0) g.StartTime = ev.Ts;
                        g._desc!.LastStartTime = ev.Ts;
                        break;
                    case EventType.GoEnd:
                    case EventType.GoStop:
                        g = gs[ev.G];
                        g.Finalize(ev.Ts, gcStartTime, ev);
                        break;
                    case EventType.GoBlockSend:
                    case EventType.GoBlockRecv:
                    case EventType.GoBlockSelect:
                    case EventType.GoBlockSync:
                    case EventType.GoBlockCond:
                        g = gs[ev.G];
                        g._stat.ExecTime += ev.Ts - g._desc!.LastStartTime;
                        g._desc!.LastStartTime = 0;
                        g._desc!.BlockSyncTime = ev.Ts;
                        break;
                    case EventType.GoSched:
                    case EventType.GoPreempt:
                        g = gs[ev.G];
                        g._stat.ExecTime += ev.Ts - g._desc!.LastStartTime;
                        g._desc!.LastStartTime = 0;
                        g._desc!.BlockSchedTime = ev.Ts;
                        break;
                    case EventType.GoSleep:
                    case EventType.GoBlock:
                        g = gs[ev.G];
                        g._stat.ExecTime += ev.Ts - g._desc!.LastStartTime;
                        g._desc!.LastStartTime = 0;
                        break;
                    case EventType.GoBlockNet:
                        g = gs[ev.G];
                        g._stat.ExecTime += ev.Ts - g._desc!.LastStartTime;
                        g._desc!.LastStartTime = 0;
                        g._desc!.BlockNetTime = ev.Ts;
                        break;
                    case EventType.GoBlockGc:
                        g = gs[ev.G];
                        g._stat.ExecTime += ev.Ts - g._desc!.LastStartTime;
                        g._desc!.LastStartTime = 0;
                        g._desc!.BlockGcTime = ev.Ts;
                        break;
                    case EventType.GoUnblock:
                        g = gs[ev.Args[0]];
                        if (g._desc!.BlockNetTime != 0)
                        {
                            g._stat.IoTime += ev.Ts - g._desc!.BlockNetTime;
                            g._desc!.BlockNetTime = 0;
                        }

                        if (g._desc!.BlockSyncTime != 0)
                        {
                            g._stat.BlockTime += ev.Ts - g._desc!.BlockSyncTime;
                            g._desc!.BlockSyncTime = 0;
                        }

                        g._desc.BlockSchedTime = ev.Ts;
                        break;
                    case EventType.GoSysBlock:
                        g = gs[ev.G];
                        g._stat.ExecTime += ev.Ts - g._desc!.LastStartTime;
                        g._desc!.LastStartTime = 0;
                        g._desc!.BlockSyscallTime = ev.Ts;
                        break;
                    case EventType.GoSysExit:
                        g = gs[ev.G];
                        if (g._desc!.BlockSyscallTime != 0)
                        {
                            g._stat.SyscallTime += ev.Ts - g._desc!.BlockSyscallTime;
                            g._desc!.BlockSyscallTime = 0;
                        }

                        g._desc!.BlockSchedTime = ev.Ts;
                        break;
                    case EventType.GcSweepStart:
                    {
                        if (gs.TryGetValue(ev.G, out var gg)) gg._desc!.BlockSweepTime = ev.Ts;
                        break;
                    }
                    case EventType.GcSweepDone:
                    {
                        if (gs.TryGetValue(ev.G, out var gg))
                        {
                            gg._stat.SweepTime += ev.Ts - gg._desc!.BlockSweepTime;
                            gg._desc!.BlockSweepTime = 0;
                        }

                        break;
                    }
                    case EventType.GcMarkAssistStart:
                    {
                        if (gs.TryGetValue(ev.G, out var gg)) gg._desc!.BlockAssitMarkTime = ev.Ts;
                        break;
                    }
                    case EventType.GcMarkAssistDone:
                    {
                        if (gs.TryGetValue(ev.G, out var gg))
                        {
                            gg._stat.AssitMarkTime += ev.Ts - gg._desc!.BlockAssitMarkTime;
                            gg._desc!.BlockAssitMarkTime = 0;
                        }
                        break;
                    }
                    case EventType.GcStart:
                        gcStartTime = ev.Ts;
                        break;
                    case EventType.GcDone:
                    {
                        foreach (var (_, gg) in gs.Where(kv => !kv.Value.Terminated))
                            gg._stat.GcTime = ev.Ts - Math.Max(gcStartTime, gg.CreationTime);
                        gcStartTime = 0;
                        break;
                    }
                    case EventType.UserRegion:
                        g = gs[ev.G];
                        switch (ev.Args[1])
                        {
                            case 0: // region start
                                var stat = g.SnapshotStat(lastTs, gcStartTime);
                                var region = new UserRegion(ev.Args[0], g.Id, ev.StringArgs![0], ev, stat);
                                g._desc!.ActiveRegions.Add(region);
                                break;
                            case 1: // region end
                                UserRegion sd;
                                var stack = g._desc!.ActiveRegions;
                                if (stack.Count > 0)
                                {
                                    sd = stack[^1];
                                    stack.RemoveAt(stack.Count - 1);
                                }
                                else
                                {
                                    sd = new UserRegion(ev.Args[0], g.Id, ev.StringArgs![0]);
                                }

                                sd.Stat = g.SnapshotStat(lastTs, gcStartTime) - sd.Stat;
                                sd.End = ev;
                                g._regions.Add(sd);
                                break;
                        }

                        break;
                }
            }

            foreach (var (_, g) in gs)
            {
                g.Finalize(lastTs, gcStartTime, null);

                g._regions.Sort((i, j) =>
                {
                    var x = i.Start;
                    var y = j.Start;
                    if (x == null) return -1;
                    return y == null ? 1 : x.Ts.CompareTo(y.Ts);
                });

                g._desc = null;
            }

            return gs;
        }

        private ExecutionStat SnapshotStat(long lastTs, long activeGcStartTime)
        {
            var ret = Stat;

            // finalized GDesc. No pending state.
            if (_desc == null) return ret;

            if (activeGcStartTime != 0)
                ret.GcTime += lastTs - Math.Max(CreationTime, activeGcStartTime);

            if (Stat.TotalTime == 0) ret.TotalTime = lastTs - CreationTime;
            if (_desc.LastStartTime != 0) ret.ExecTime += lastTs - _desc.LastStartTime;
            if (_desc.BlockNetTime != 0) ret.IoTime += lastTs - _desc.BlockNetTime;
            if (_desc.BlockSyncTime != 0) ret.BlockTime += lastTs - _desc.BlockSyncTime;
            if (_desc.BlockSyscallTime != 0) ret.SyscallTime += lastTs - _desc.BlockSyscallTime;
            if (_desc.BlockSchedTime != 0) ret.SchedWaitTime += lastTs - _desc.BlockSchedTime;
            if (_desc.BlockSweepTime != 0) ret.SweepTime += lastTs - _desc.BlockSweepTime;
            if (_desc.BlockAssitMarkTime != 0) ret.AssitMarkTime += lastTs - _desc.BlockAssitMarkTime;

            return ret;
        }

        private void Finalize(long lastTs, long activeGcStartTime, TraceEvent? trigger)
        {
            EndTime = trigger?.Ts;
            _stat = SnapshotStat(lastTs, activeGcStartTime);

            foreach (var region in _desc!.ActiveRegions)
            {
                region.End = trigger;
                region.Stat = Stat - region.Stat;
                _regions.Add(region);
            }

            _desc = new Desc();
        }

        private class Desc
        {
            public long LastStartTime { get; set; }
            public long BlockNetTime { get; set; }
            public long BlockSyncTime { get; set; }
            public long BlockSyscallTime { get; set; }
            public long BlockSweepTime { get; set; }
            public long BlockAssitMarkTime { get; set; }
            public long BlockGcTime { get; set; }
            public long BlockSchedTime { get; set; }
            public List<UserRegion> ActiveRegions { get; set; } = new List<UserRegion>();
        }
    }
}