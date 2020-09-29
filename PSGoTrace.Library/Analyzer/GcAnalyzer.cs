using System;
using System.Collections.Generic;
using System.Linq;
using PSGoTrace.Library.Parser;

namespace PSGoTrace.Library.Analyzer
{
    public readonly struct MutatorUtil
    {
        public long Time { get; }
        public double Util { get; }

        public MutatorUtil(long time, double util)
        {
            Time = time;
            Util = util;
        }
    }

    public class GcAnalyzer
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

        private readonly IList<TraceEvent> _events;

        public GcAnalyzer(IList<TraceEvent> events)
        {
            if (events.Count == 0) throw new ArgumentException("trace contains no events");
            _events = events;
        }

        public IList<IList<MutatorUtil>> MutatorUtilization(Option option)
        {
            var ps = new List<(int gc, int series)>();
            var stw = 0;
            var result = new List<IList<MutatorUtil>>();
            var assists = new HashSet<ulong>();
            var bgMark = new HashSet<ulong>();
            var block = new Dictionary<ulong, TraceEvent>();

            MutatorUtil mu;
            foreach (var ev in _events)
            {
                var psSpan = ps;
                switch (ev.Type)
                {
                    case EventType.Gomaxprocs:
                    {
                        var gomaxprocs = (int) ev.Args[0];
                        if (ps.Count > gomaxprocs)
                        {
                            if ((option & Option.PerProc) != 0)
                                ps.Skip(gomaxprocs).ForEach(p => AddUtil(result[p.series], new MutatorUtil(ev.Ts, 0)));

                            ps.RemoveRange(gomaxprocs, ps.Count - gomaxprocs);
                        }

                        while (ps.Count < gomaxprocs)
                        {
                            var series = 0;
                            if ((option & Option.PerProc) != 0 || result.Count == 0)
                            {
                                series = result.Count;
                                result.Add(new List<MutatorUtil> {new MutatorUtil(ev.Ts, 1)});
                            }

                            ps.Add((0, series));
                        }

                        break;
                    }
                    case EventType.GcStwStart:
                    {
                        if ((option & Option.Stw) != 0) stw++;
                        break;
                    }
                    case EventType.GcStwDone:
                    {
                        if ((option & Option.Stw) != 0) stw--;
                        break;
                    }
                    case EventType.GcMarkAssistStart:
                    {
                        if ((option & Option.Assist) != 0)
                        {
                            ps[ev.P] = (ps[ev.P].gc + 1, ps[ev.P].series);
                            assists.Add(ev.G);
                        }

                        break;
                    }
                    case EventType.GcMarkAssistDone:
                    {
                        if ((option & Option.Assist) != 0)
                        {
                            ps[ev.P] = (ps[ev.P].gc - 1, ps[ev.P].series);
                            assists.Remove(ev.G);
                        }

                        break;
                    }
                    case EventType.GcSweepStart:
                    {
                        if ((option & Option.Sweep) != 0)
                            ps[ev.P] = (ps[ev.P].gc + 1, ps[ev.P].series);
                        break;
                    }
                    case EventType.GcSweepDone:
                    {
                        if ((option & Option.Sweep) != 0)
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
                        if ((option & Option.Background) != 0 &&
                            ev.StringArgs![0].StartsWith("GC ") &&
                            ev.StringArgs![0] != "GC (idle)" &&
                            !((option & Option.PerProc) != 0 && ev.StringArgs![0] == "GC (dedicated)"))
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

                if ((option & Option.PerProc) == 0)
                {
                    if (ps.Count == 0) continue;
                    var gcPs = stw > 0 ? ps.Count : ps.Count(v => v.gc > 0);
                    mu = new MutatorUtil(ev.Ts, 1 - gcPs / (double) ps.Count);
                    AddUtil(result[0], mu);
                }
                else
                {
                    foreach (var (gc, series) in ps)
                    {
                        var util = stw > 0 || gc > 0 ? 0 : 1;
                        AddUtil(result[series], new MutatorUtil(ev.Ts, util));
                    }
                }
            }

            // Add final 0 utilization event to any remaining series. This
            // is important to mark the end of the trace. The exact value
            // shouldn't matter since no window should extend beyond this,
            // but using 0 is symmetric with the start of the trace.
            mu = new MutatorUtil(_events[^1].Ts, 0);
            ps.ForEach(p => AddUtil(result[p.series], mu));
            return result;
        }

        private static void AddUtil(IList<MutatorUtil> utils, MutatorUtil mu)
        {
            if (utils.Count > 0)
            {
                if (Math.Abs(mu.Util - utils[^1].Util) < 1e-5) return;
                if (mu.Time == utils[^1].Time)
                {
                    if (mu.Util < utils[^1].Util) utils[^1] = mu;
                    return;
                }
            }

            utils.Add(mu);
        }
    }
}