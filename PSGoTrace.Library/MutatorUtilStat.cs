using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using PSGoTrace.Library.Types;

namespace PSGoTrace.Library
{
    public class MutatorUtilStat : IReadOnlyList<MutatorUtilSeries>
    {
        [Flags]
        public enum Option
        {
            /// <summary>
            ///     utilization should account for STW events.
            /// </summary>
            Stw = 1 << 1,

            /// <summary>
            ///     utilization should account for background mark workers.
            /// </summary>
            Background = 1 << 2,

            /// <summary>
            ///     utilization should account for mark.
            /// </summary>
            Assist = 1 << 3,

            /// <summary>
            ///     utilization should account for sweeping.
            /// </summary>
            Sweep = 1 << 4,

            /// <summary>
            ///     each P should be given a separate utilization function.
            ///     Otherwise, there is a single function and each P is given a fraction of the utilization.
            /// </summary>
            PerProc = 1 << 5
        }

        private readonly Option _option;

        private readonly IList<MutatorUtilSeries> _utils;

        public MutatorUtilStat(Option prop, IList<TraceEvent> events)
        {
            if (events.Count == 0) throw new ArgumentException("Events cannot be empty");
            _option = prop;
            _utils = new List<MutatorUtilSeries>();

            var ps = new List<(int gc, int series)>();
            var stw = 0;
            var assists = new HashSet<ulong>();
            var bgMark = new HashSet<ulong>();
            var block = new Dictionary<ulong, TraceEvent>();

            MutatorUtil mu;
            foreach (var ev in events)
            {
                switch (ev.Type)
                {
                    case EventType.Gomaxprocs:
                    {
                        var gomaxprocs = (int) ev.Args[0];
                        if (ps.Count > gomaxprocs)
                        {
                            if ((prop & Option.PerProc) != 0)
                                ps.Skip(gomaxprocs).ForEach(p => _utils[p.series].Add(new MutatorUtil(ev.Ts, 0)));

                            ps.RemoveRange(gomaxprocs, ps.Count - gomaxprocs);
                        }

                        while (ps.Count < gomaxprocs)
                        {
                            var series = 0;
                            if ((prop & Option.PerProc) != 0 || _utils.Count == 0)
                            {
                                series = _utils.Count;
                                _utils.Add(new MutatorUtilSeries {new MutatorUtil(ev.Ts, 1)});
                            }

                            ps.Add((0, series));
                        }

                        break;
                    }
                    case EventType.GcStwStart:
                    {
                        if ((prop & Option.Stw) != 0) stw++;
                        break;
                    }
                    case EventType.GcStwDone:
                    {
                        if ((prop & Option.Stw) != 0) stw--;
                        break;
                    }
                    case EventType.GcMarkAssistStart:
                    {
                        if ((prop & Option.Assist) != 0)
                        {
                            ps[ev.P] = (ps[ev.P].gc + 1, ps[ev.P].series);
                            assists.Add(ev.G);
                        }

                        break;
                    }
                    case EventType.GcMarkAssistDone:
                    {
                        if ((prop & Option.Assist) != 0)
                        {
                            ps[ev.P] = (ps[ev.P].gc - 1, ps[ev.P].series);
                            assists.Remove(ev.G);
                        }

                        break;
                    }
                    case EventType.GcSweepStart:
                    {
                        if ((prop & Option.Sweep) != 0)
                            ps[ev.P] = (ps[ev.P].gc + 1, ps[ev.P].series);
                        break;
                    }
                    case EventType.GcSweepDone:
                    {
                        if ((prop & Option.Sweep) != 0)
                            ps[ev.P] = (ps[ev.P].gc - 1, ps[ev.P].series);
                        break;
                    }
                    case EventType.GoStartLabel:
                    {
                        // Background mark worker.
                        //
                        // If we're in per-proc mode, we don't
                        // count dedicated workers because
                        // they kick all of the goroutines off
                        // that P, so don't directly
                        // contribute to goroutine latency.
                        if ((prop & Option.Background) != 0 &&
                            ev.StringArgs![0].StartsWith("GC ") &&
                            ev.StringArgs![0] != "GC (idle)" &&
                            !((prop & Option.PerProc) != 0 && ev.StringArgs![0] == "GC (dedicated)"))
                        {
                            bgMark.Add(ev.G);
                            ps[ev.P] = (ps[ev.P].gc + 1, ps[ev.P].series);
                        }

                        goto case EventType.GoStart;
                    }
                    case EventType.GoStart:
                    {
                        if (assists.Contains(ev.G))
                            ps[ev.P] = (ps[ev.P].gc + 1, ps[ev.P].series);
                        block[ev.G] = ev.Link!;
                        break;
                    }
                    default:
                    {
                        if (block.TryGetValue(ev.G, out var blockEv) && ev != blockEv)
                            continue;
                        if (assists.Contains(ev.G))
                            ps[ev.P] = (ps[ev.P].gc - 1, ps[ev.P].series);
                        if (bgMark.Contains(ev.G))
                        {
                            ps[ev.P] = (ps[ev.P].gc - 1, ps[ev.P].series);
                            bgMark.Remove(ev.G);
                        }

                        block.Remove(ev.G);
                        break;
                    }
                }

                if ((prop & Option.PerProc) == 0)
                {
                    if (ps.Count == 0) continue;
                    var gcPs = stw > 0 ? ps.Count : ps.Count(v => v.gc > 0);
                    mu = new MutatorUtil(ev.Ts, 1 - gcPs / (double) ps.Count);
                    _utils[0].Add(mu);
                }
                else
                {
                    foreach (var (gc, series) in ps)
                    {
                        var util = stw > 0 || gc > 0 ? 0 : 1;
                        _utils[series].Add(new MutatorUtil(ev.Ts, util));
                    }
                }
            }

            // Add final 0 utilization event to any remaining series. This
            // is important to mark the end of the trace. The exact value
            // shouldn't matter since no window should extend beyond this,
            // but using 0 is symmetric with the start of the trace.
            mu = new MutatorUtil(events[^1].Ts, 0);
            ps.ForEach(p => _utils[p.series].Add(mu));
        }

        public bool IncludeStw => (_option & Option.Stw) != 0;
        public bool IncludeBackground => (_option & Option.Background) != 0;
        public bool IncludeAssist => (_option & Option.Assist) != 0;
        public bool IncludeSweep => (_option & Option.Sweep) != 0;
        public bool IsPerProc => (_option & Option.PerProc) != 0;
        public int ProcCount => _utils.Count;
        public IEnumerator<MutatorUtilSeries> GetEnumerator() => _utils.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable) _utils).GetEnumerator();
        public int Count => _utils.Count;

        public MutatorUtilSeries this[int index] => _utils[index];
    }
}