using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using PSGoTrace.Library.Types;

namespace PSGoTrace.Library
{
    public class GoroutinesStat : IReadOnlyDictionary<ulong, Goroutine>
    {
        private readonly Dictionary<ulong, Goroutine> _goroutines;

        public GoroutinesStat(IEnumerable<TraceEvent> events)
        {
            var gs = new Dictionary<ulong, GoroutineBuilder>();
            long lastTs = 0, gcStartTime = 0;
            foreach (var ev in events)
            {
                lastTs = ev.Ts;
                switch (ev.Type)
                {
                    case EventType.GoCreate:
                    {
                        var g = new GoroutineBuilder(ev.Args[0], ev.Ts);
                        g.Pending.BlockSchedTime = ev.Ts;

                        // When a goroutine is newly created, inherit the
                        // task of the active region. For ease handling of
                        // this case, we create a fake region description with
                        // the task id.
                        if (gs.TryGetValue(ev.G, out var creatorG))
                        {
                            var active = creatorG.Pending.ActiveRegions;
                            if (active.Count > 0)
                            {
                                var s = active[^1];
                                if (s.TaskId != 0)
                                    g.Pending.ActiveRegions = new List<UserRegion>
                                        {new UserRegion(s.TaskId, g.Id, start: ev)};
                            }
                        }

                        gs[g.Id] = g;
                        break;
                    }
                    case EventType.GoStart:
                    case EventType.GoStartLabel:
                    {
                        var g = gs[ev.G];
                        if (g.Pc == 0)
                        {
                            g.Pc = ev.Stack![0].Pc;
                            g.Name = ev.Stack[0].Fn;
                        }

                        if (g.Pending.BlockSchedTime != 0)
                        {
                            g.Stat.SchedWaitTime += ev.Ts - g.Pending.BlockSchedTime;
                            g.Pending.BlockSchedTime = 0;
                        }

                        if (g.StartTime == 0) g.StartTime = ev.Ts;
                        g.Pending.LastStartTime = ev.Ts;
                        break;
                    }
                    case EventType.GoEnd:
                    case EventType.GoStop:
                    {
                        var g = gs[ev.G];
                        g.Finalize(ev.Ts, gcStartTime, ev);
                        break;
                    }
                    case EventType.GoBlockSend:
                    case EventType.GoBlockRecv:
                    case EventType.GoBlockSelect:
                    case EventType.GoBlockSync:
                    case EventType.GoBlockCond:
                    {
                        var g = gs[ev.G];
                        g.Stat.ExecTime += ev.Ts - g.Pending.LastStartTime;
                        g.Pending.LastStartTime = 0;
                        g.Pending.BlockSyncTime = ev.Ts;
                        break;
                    }
                    case EventType.GoSched:
                    case EventType.GoPreempt:
                    {
                        var g = gs[ev.G];
                        g.Stat.ExecTime += ev.Ts - g.Pending.LastStartTime;
                        g.Pending.LastStartTime = 0;
                        g.Pending.BlockSchedTime = ev.Ts;
                        break;
                    }
                    case EventType.GoSleep:
                    case EventType.GoBlock:
                    {
                        var g = gs[ev.G];
                        g.Stat.ExecTime += ev.Ts - g.Pending.LastStartTime;
                        g.Pending.LastStartTime = 0;
                        break;
                    }
                    case EventType.GoBlockNet:
                    {
                        var g = gs[ev.G];
                        g.Stat.ExecTime += ev.Ts - g.Pending.LastStartTime;
                        g.Pending.LastStartTime = 0;
                        g.Pending.BlockNetTime = ev.Ts;
                        break;
                    }
                    case EventType.GoBlockGc:
                    {
                        var g = gs[ev.G];
                        g.Stat.ExecTime += ev.Ts - g.Pending.LastStartTime;
                        g.Pending.LastStartTime = 0;
                        g.Pending.BlockGcTime = ev.Ts;
                        break;
                    }
                    case EventType.GoUnblock:
                    {
                        var g = gs[ev.Args[0]];
                        if (g.Pending.BlockNetTime != 0)
                        {
                            g.Stat.IoTime += ev.Ts - g.Pending.BlockNetTime;
                            g.Pending.BlockNetTime = 0;
                        }

                        if (g.Pending.BlockSyncTime != 0)
                        {
                            g.Stat.BlockTime += ev.Ts - g.Pending.BlockSyncTime;
                            g.Pending.BlockSyncTime = 0;
                        }

                        g.Pending.BlockSchedTime = ev.Ts;
                        break;
                    }
                    case EventType.GoSysBlock:
                    {
                        var g = gs[ev.G];
                        g.Stat.ExecTime += ev.Ts - g.Pending.LastStartTime;
                        g.Pending.LastStartTime = 0;
                        g.Pending.BlockSyscallTime = ev.Ts;
                        break;
                    }
                    case EventType.GoSysExit:
                    {
                        var g = gs[ev.G];
                        if (g.Pending.BlockSyscallTime != 0)
                        {
                            g.Stat.SyscallTime += ev.Ts - g.Pending.BlockSyscallTime;
                            g.Pending.BlockSyscallTime = 0;
                        }

                        g.Pending.BlockSchedTime = ev.Ts;
                        break;
                    }
                    case EventType.GcSweepStart:
                    {
                        if (gs.TryGetValue(ev.G, out var gg))
                            gg.Pending.BlockSweepTime = ev.Ts;
                        break;
                    }
                    case EventType.GcSweepDone:
                    {
                        if (gs.TryGetValue(ev.G, out var gg))
                        {
                            gg.Stat.SweepTime += ev.Ts - gg.Pending.BlockSweepTime;
                            gg.Pending.BlockSweepTime = 0;
                        }

                        break;
                    }
                    case EventType.GcMarkAssistStart:
                    {
                        if (gs.TryGetValue(ev.G, out var gg))
                            gg.Pending.BlockAssitMarkTime = ev.Ts;
                        break;
                    }
                    case EventType.GcMarkAssistDone:
                    {
                        if (gs.TryGetValue(ev.G, out var gg))
                        {
                            gg.Stat.AssitMarkTime += ev.Ts - gg.Pending.BlockAssitMarkTime;
                            gg.Pending.BlockAssitMarkTime = 0;
                        }

                        break;
                    }
                    case EventType.GcStart:
                    {
                        gcStartTime = ev.Ts;
                        break;
                    }
                    case EventType.GcDone:
                    {
                        foreach (var (_, gg) in gs.Where(kv => !kv.Value.Terminated))
                            gg.Stat.GcTime = ev.Ts - Math.Max(gcStartTime, gg.CreationTime);
                        gcStartTime = 0;
                        break;
                    }
                    case EventType.UserRegion:
                    {
                        var g = gs[ev.G];
                        switch (ev.Args[1])
                        {
                            case 0: // region start
                                var stat = g.SnapshotStat(lastTs, gcStartTime);
                                var region = new UserRegion(ev.Args[0], g.Id, ev.StringArgs![0], ev, stat);
                                g.Pending.ActiveRegions.Add(region);
                                break;
                            case 1: // region end
                                UserRegion sd;
                                var stack = g.Pending.ActiveRegions;
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
                                g.Regions.Add(sd);
                                break;
                        }

                        break;
                    }
                }
            }

            _goroutines = new Dictionary<ulong, Goroutine>(gs.Count);
            foreach (var (id, g) in gs)
            {
                g.Finalize(lastTs, gcStartTime, null);
                g.Regions.Sort((i, j) =>
                {
                    var x = i.Start;
                    var y = j.Start;
                    if (x == null) return -1;
                    return y == null ? 1 : x.Ts.CompareTo(y.Ts);
                });

                _goroutines[id] = g.Build();
            }
        }

        public IEnumerator<KeyValuePair<ulong, Goroutine>> GetEnumerator() => _goroutines.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable) _goroutines).GetEnumerator();

        public int Count => _goroutines.Count;

        public bool ContainsKey(ulong key) => _goroutines.ContainsKey(key);

        public bool TryGetValue(ulong key, out Goroutine value) => _goroutines.TryGetValue(key, out value);

        public Goroutine this[ulong key] => _goroutines[key];

        public IEnumerable<ulong> Keys => _goroutines.Keys;

        public IEnumerable<Goroutine> Values => _goroutines.Values;

        private class GoroutineBuilder
        {
            private PendingStates _pending;
            private ExecutionStat _stat;

            public GoroutineBuilder(ulong id, long creationTime)
            {
                Id = id;
                Name = "";
                CreationTime = creationTime;
            }

            public ulong Id { get; }
            public string Name { get; internal set; }
            public ulong Pc { get; internal set; }
            public long CreationTime { get; }
            public long StartTime { get; internal set; }
            public long? EndTime { get; internal set; }

            public List<UserRegion> Regions { get; } = new List<UserRegion>();

            public ref ExecutionStat Stat => ref _stat;

            public ref PendingStates Pending => ref _pending;

            public bool Terminated => EndTime.HasValue;

            public ExecutionStat SnapshotStat(long lastTs, long activeGcStartTime)
            {
                var ret = Stat;

                if (activeGcStartTime != 0)
                    ret.GcTime += lastTs - Math.Max(CreationTime, activeGcStartTime);

                if (Stat.TotalTime == 0) ret.TotalTime = lastTs - CreationTime;
                if (Pending.LastStartTime != 0) ret.ExecTime += lastTs - Pending.LastStartTime;
                if (Pending.BlockNetTime != 0) ret.IoTime += lastTs - Pending.BlockNetTime;
                if (Pending.BlockSyncTime != 0) ret.BlockTime += lastTs - Pending.BlockSyncTime;
                if (Pending.BlockSyscallTime != 0) ret.SyscallTime += lastTs - Pending.BlockSyscallTime;
                if (Pending.BlockSchedTime != 0) ret.SchedWaitTime += lastTs - Pending.BlockSchedTime;
                if (Pending.BlockSweepTime != 0) ret.SweepTime += lastTs - Pending.BlockSweepTime;
                if (Pending.BlockAssitMarkTime != 0) ret.AssitMarkTime += lastTs - Pending.BlockAssitMarkTime;

                return ret;
            }

            public void Finalize(long lastTs, long activeGcStartTime, TraceEvent? trigger)
            {
                EndTime = trigger?.Ts;
                Stat = SnapshotStat(lastTs, activeGcStartTime);

                foreach (var region in Pending!.ActiveRegions)
                {
                    region.End = trigger;
                    region.Stat = Stat - region.Stat;
                    Regions.Add(region);
                }

                Pending.Reset();
            }

            public Goroutine Build() =>
                new Goroutine(Id, Name, Pc, CreationTime, StartTime, EndTime, Regions, Stat);
        }

        private struct PendingStates
        {
            public void Reset()
            {
                LastStartTime = 0;
                BlockGcTime = 0;
                BlockSyncTime = 0;
                BlockSyncTime = 0;
                BlockSweepTime = 0;
                BlockAssitMarkTime = 0;
                BlockGcTime = 0;
                BlockSchedTime = 0;
                ActiveRegions = new List<UserRegion>();
            }

            public long LastStartTime { get; set; }
            public long BlockNetTime { get; set; }
            public long BlockSyncTime { get; set; }
            public long BlockSyscallTime { get; set; }
            public long BlockSweepTime { get; set; }
            public long BlockAssitMarkTime { get; set; }
            public long BlockGcTime { get; set; }
            public long BlockSchedTime { get; set; }
            public List<UserRegion> ActiveRegions { get; set; }
        }
    }
}