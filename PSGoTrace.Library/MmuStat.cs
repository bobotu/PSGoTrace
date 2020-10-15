using System;
using System.Collections.Generic;
using System.Linq;
using PSGoTrace.Library.Util;

namespace PSGoTrace.Library
{
    public class MmuStat
    {
        private readonly MmuSeries[] _series;

        public MmuStat(MutatorUtilStat utils)
        {
            _series = utils.Select(util => new MmuSeries(util)).ToArray();
        }

        /// <summary>
        ///     returns the minimum mutator utilization for the given time window.
        ///     This is the minimum utilization for all windows of this
        ///     duration across the execution. The returned value is in the range [0, 1].
        /// </summary>
        /// <param name="window">window size</param>
        /// <returns>MMU for the given time window</returns>
        public double CalcMmu(long window) => new MutatorStats(_series, window).Mmu;

        /// <summary>
        ///     returns n specific examples of the lowest mutator
        ///     utilization for the given window size. The returned windows will be
        ///     disjoint (otherwise there would be a huge number of
        ///     mostly-overlapping windows at the single lowest point). There are
        ///     no guarantees on which set of disjoint windows this returns.
        /// </summary>
        /// <param name="window">window size</param>
        /// <param name="n">number of windows</param>
        /// <returns>n specific examples of the lowest mutator utilization for the given window size</returns>
        public UtilWindow[] CalcExamples(long window, int n)
        {
            var stat = new MutatorStats(_series, window, nWorst: n);
            var windows = stat.TrackedWindows;
            Array.Sort(windows, UtilWindow.Comparer);
            return windows;
        }

        public double[] CalcMud(long window, double[] quantiles)
        {
            if (quantiles.Length == 0) return new double[0];

            // Each unrefined band contributes a known total mass to the
            // distribution (bandDur except at the end), but in an unknown
            // way. However, we know that all the mass it contributes must
            // be at or above its worst-case mean mutator utilization.
            //
            // Hence, we refine bands until the highest desired
            // distribution quantile is less than the next worst-case mean
            // mutator utilization. At this point, all further
            // contributions to the distribution must be beyond the
            // desired quantile and hence cannot affect it.
            //
            // First, find the highest desired distribution quantile.
            var maxQ = quantiles.Max();

            // The distribution's mass is in units of time (it's not
            // normalized because this would make it more annoying to
            // account for future contributions of unrefined bands). The
            // total final mass will be the duration of the trace itself
            // minus the window size. Using this, we can compute the mass
            // corresponding to quantile maxQ.
            var duration = 0L;
            for (var i = 0; i < _series.Length; i++)
            {
                ref var s = ref _series[i];
                var d = s.Util(^1).Time - s.Util(0).Time;
                if (d >= window) duration += d - window;
            }

            var qMass = duration * maxQ;
            var stat = new MutatorStats(_series, window, mud: new Mud {TrackMass = qMass});
            var result = new double[quantiles.Length];
            for (var i = 0; i < quantiles.Length; i++)
            {
                var y = duration * quantiles[i];
                // There are a few legitimate ways the result will be null:
                //
                // 1. If the window is the full trace
                // duration, then the windowed MU function is
                // only defined at a single point, so the MU
                // distribution is not well-defined.
                //
                // 2. If there are no events, then the MU
                // distribution has no mass.
                //
                // Either way, all of the quantiles will have
                // converged toward the MMU at this point.
                result[i] = stat.Mud?.InvCumulativeSum(y) ?? stat.Mmu;
            }

            return result;
        }

        public readonly struct UtilWindow
        {
            private sealed class UtilComparer : IComparer<UtilWindow>
            {
                private readonly bool _revers;

                public UtilComparer(bool revers)
                {
                    _revers = revers;
                }

                public int Compare(UtilWindow x, UtilWindow y) => _revers ? -CompareRaw(x, y) : CompareRaw(x, y);

                private static int CompareRaw(UtilWindow x, UtilWindow y)
                {
                    var cmp = x.MutatorUtil.CompareTo(y.MutatorUtil);
                    return cmp != 0 ? cmp : x.Time.CompareTo(y.Time);
                }
            }

            public static IComparer<UtilWindow> Comparer { get; } = new UtilComparer(false);
            public static IComparer<UtilWindow> ReversComparer { get; } = new UtilComparer(true);

            public UtilWindow(long time, double mutatorUtil)
            {
                Time = time;
                MutatorUtil = mutatorUtil;
            }

            public long Time { get; }
            public double MutatorUtil { get; }
        }

        private class MutatorStats
        {
            private readonly int _nWorst;
            private readonly BinaryHeap<UtilWindow> _wHeap = new BinaryHeap<UtilWindow>(UtilWindow.ReversComparer);
            private double _lastMu;
            private long _lastTime;

            public MutatorStats(
                MmuSeries[] series, long window,
                double mmu = 1.0, double bound = 1.0,
                int nWorst = default, Mud? mud = null)
            {
                Mmu = mmu;
                Bound = bound;
                _nWorst = nWorst;
                Mud = mud;

                if (window <= 0)
                {
                    Mmu = 0;
                    return;
                }

                var bandUtils = new List<BandUtil>();
                var windows = new long[series.Length];
                for (var i = 0; i < series.Length; i++)
                {
                    ref var s = ref series[i];
                    windows[i] = Math.Min(window, s.Util(^1).Time - s.Util(0).Time);
                    bandUtils.AddRange(s.CalcBandUtils(i, windows[i]));
                }

                var utilHeap = new BinaryHeap<BandUtil>(bandUtils);
                while (utilHeap.Count > 0 && utilHeap.Head.UtilBound < Bound)
                {
                    var head = utilHeap.Head;
                    var i = head.Series;
                    series[i].CalcBandMmu(head.Index, windows[i], this);
                    utilHeap.Pop();
                }
            }

            public double Bound { get; private set; }
            public double Mmu { get; private set; }
            public Mud? Mud { get; }
            public UtilWindow[] TrackedWindows => _wHeap.ToArray();

            /// <summary>
            ///     resetTime declares a discontinuity in the windowed mutator
            ///     utilization function by resetting the current time.
            /// </summary>
            public void ResetTime()
            {
                _lastTime = long.MaxValue;
            }

            /// <summary>
            ///     ddMU adds a point to the windowed mutator utilization function at (time, mu).
            ///     This must be called for monotonically increasing values of time.
            /// </summary>
            /// <returns>true if further calls to addMU would be pointless.</returns>
            public bool AddMu(long time, double mu, long window)
            {
                Bound = Mmu = Math.Min(Mmu, mu);
                // If the minimum has reached zero, it can't go any lower, so we can stop early.
                if (_nWorst == 0) return mu == 0;

                void TryAddWindow()
                {
                    foreach (var (i, w) in _wHeap.Elements())
                    {
                        if (time + window <= w.Time || w.Time + window <= time) continue;
                        if (w.MutatorUtil <= mu) return;
                        _wHeap.Remove(i);
                        break;
                    }

                    _wHeap.Add(new UtilWindow(time, mu));
                    if (_wHeap.Count > _nWorst) _wHeap.Pop();
                }

                // Consider adding this window to the n worst.
                if (_wHeap.Count < _nWorst || mu < _wHeap.Head.MutatorUtil) TryAddWindow();
                Bound = _wHeap.Count < _nWorst ? 1.0 : Math.Max(Bound, _wHeap.Head.MutatorUtil);
                if (Mud == null) return _wHeap.Count == _nWorst && _wHeap.Head.MutatorUtil == 0;

                if (_lastTime != long.MaxValue)
                    Mud.Add(_lastMu, mu, time - _lastTime);
                _lastTime = time;
                _lastMu = mu;
                Bound = Mud.ApproxInvCumulativeSum(out _, out var mudBound) ? Math.Max(Bound, mudBound) : 1;

                // It's not worth checking percentiles every time, so just keep accumulating this band.
                return false;
            }
        }

        private readonly struct MmuSeries
        {
            private readonly long _bandDur;
            private readonly double[] _sums;
            private readonly MmuBand[] _bands;
            private readonly MutatorUtilSeries _utils;

            public MmuSeries(MutatorUtilSeries utils)
            {
                if (utils.Count == 0) throw new ArgumentException("utils is empty");
                _utils = utils;
                _sums = new double[utils.Count];
                var prev = new MutatorUtil();
                var sum = .0;
                for (var i = 0; i < utils.Count; i++)
                {
                    var u = utils[i];
                    sum += TotalUtilOf(prev.Util, u.Time - prev.Time);
                    _sums[i] = sum;
                    prev = u;
                }

                // Divide the utilization curve up into equal size
                // non-overlapping "bands" and compute a summary for each of
                // these bands.
                //
                // Compute the duration of each band.
                var numBands = Math.Min(1000, utils.Count);
                var dur = utils[^1].Time - utils[0].Time;
                _bandDur = Math.Max((dur + numBands - 1) / numBands, 1);

                // Compute the bands. There are numBands+1 bands in order to
                // record the final cumulative sum.
                _bands = new MmuBand[numBands + 1];
                var pos = 0;
                for (var i = 0; i < numBands + 1; i++)
                {
                    var (startTime, endTime) = BandTime(i);
                    var cumUtil = Advance(ref pos, startTime);
                    var minUtil = 1.0;
                    for (var j = pos; j < utils.Count && utils[j].Time < endTime; j++)
                        minUtil = Math.Min(minUtil, utils[j].Util);
                    _bands[i] = new MmuBand(minUtil, cumUtil, pos);
                }
            }

            public MutatorUtil Util(Index i) => _utils[i];

            public BandUtil[] CalcBandUtils(int series, long window)
            {
                // For each band, compute the worst-possible total mutator
                // utilization for all windows that start in that band.

                // minBands is the minimum number of bands a window can span
                // and maxBands is the maximum number of bands a window can
                // span in any alignment.
                var minBands = (window + _bandDur - 1) / _bandDur;
                var maxBands = (window + 2 * (_bandDur - 1)) / _bandDur;
                if (window > 1 && maxBands < 2) throw new ArithmeticException("maxBands < 2");
                var tailDur = window % _bandDur;
                var nUtil = Math.Max(_bands.Length - maxBands + 1, 0);
                var band = new BandUtil[nUtil];
                for (var i = 0; i < nUtil; i++)
                {
                    // To compute the worst-case MU, we assume the minimum
                    // for any bands that are only partially overlapped by
                    // some window and the mean for any bands that are
                    // completely covered by all windows.
                    var util = 0.0;

                    // Find the lowest and second lowest of the partial bands.
                    var l = _bands[i].MinUtil;
                    var r1 = _bands[i + minBands - 1].MinUtil;
                    var r2 = _bands[i + maxBands - 1].MinUtil;
                    var minBand = Math.Min(l, Math.Min(r1, r2));
                    // Assume the worst window maximally overlaps the
                    // worst minimum and then the rest overlaps the second worst minimum.
                    if (minBands == 1)
                    {
                        util += TotalUtilOf(minBand, window);
                    }
                    else
                    {
                        util += TotalUtilOf(minBand, _bandDur);
                        var midBand = minBand switch
                        {
                            var x when Math.Abs(l - x) < 1e-5 => Math.Min(r1, r2),
                            var x when Math.Abs(r1 - x) < 1e-5 => Math.Min(l, r2),
                            _ => Math.Min(l, r1)
                        };
                        util += TotalUtilOf(midBand, tailDur);
                    }

                    // Add the total mean MU of bands that are completely
                    // overlapped by all windows.
                    if (minBands > 2)
                        util += _bands[i + minBands - 1].CumUtil - _bands[i + 1].CumUtil;
                    band[i] = new BandUtil(series, i, util / window);
                }

                return band;
            }

            public void CalcBandMmu(int bandIdx, long window, MutatorStats acc)
            {
                // We think of the mutator utilization over time as the
                // box-filtered utilization function, which we call the
                // "windowed mutator utilization function". The resulting
                // function is continuous and piecewise linear (unless
                // window==0, which we handle elsewhere), where the boundaries
                // between segments occur when either edge of the window
                // encounters a change in the instantaneous mutator
                // utilization function. Hence, the minimum of this function
                // will always occur when one of the edges of the window
                // aligns with a utilization change, so these are the only
                // points we need to consider.
                //
                // We compute the mutator utilization function incrementally
                // by tracking the integral from t=0 to the left edge of the
                // window and to the right edge of the window.
                var left = _bands[bandIdx].Pos;
                var right = left;
                var (time, endTime) = BandTime(bandIdx);
                endTime = Math.Min(endTime, _utils[^1].Time - window);
                acc.ResetTime();

                while (true)
                {
                    var mu = (Advance(ref right, time + window) - Advance(ref left, time)) / window;
                    if (acc.AddMu(time, mu, window)) return;
                    if (time == endTime) return;

                    // The maximum slope of the windowed mutator
                    // utilization function is 1/window, so we can always
                    // advance the time by at least (mu - mmu) * window
                    // without dropping below mmu.
                    var minTime = time + (long) ((mu - acc.Bound) * window);
                    var t1 = Next(left, time);
                    var t2 = Next(right, time + window) - window;
                    time = Math.Max(minTime, Math.Min(t1, t2));
                    // For MMUs we could stop here, but for MUDs
                    // it's important that we span the entire band.
                    time = Math.Min(time, endTime);
                }
            }

            private long Next(int pos, long time)
            {
                for (var i = pos; i < _utils.Count; i++)
                    if (_utils[i].Time > time)
                        return _utils[i].Time;
                return (long) 1 << (63 - 1);
            }

            private double Advance(ref int pos, long time)
            {
                // Advance pos until pos+1 is time's strict successor (making
                // pos time's non-strict predecessor).
                //
                // Very often, this will be nearby, so we optimize that case,
                // but it may be arbitrarily far away, so we handled that
                // efficiently, too.
                const int maxSeq = 8;
                if (pos + maxSeq < _utils.Count && _utils[pos + maxSeq].Time > time)
                {
                    while (pos + 1 < _utils.Count && _utils[pos + 1].Time <= time) pos++;
                }
                else
                {
                    int l = pos, r = _utils.Count;
                    while (l < r)
                    {
                        var h = (l + r) >> 1;
                        if (_utils[h].Time <= time) l = h + 1;
                        else r = h;
                    }

                    pos = l - 1;
                }

                var partial = time != _utils[pos].Time ? TotalUtilOf(_utils[pos].Util, time - _utils[pos].Time) : 0;
                return _sums[pos] + partial;
            }

            private (long start, long end) BandTime(int i)
            {
                var start = i * _bandDur + _utils[0].Time;
                return (start, start + _bandDur);
            }

            private static double TotalUtilOf(double mean, long dur) => mean * dur;
        }

        private readonly struct BandUtil : IComparable<BandUtil>, IComparable
        {
            public int CompareTo(BandUtil other) => UtilBound.CompareTo(other.UtilBound);

            public int CompareTo(object? obj)
            {
                if (obj is null) return 1;
                return obj is BandUtil other
                    ? CompareTo(other)
                    : throw new ArgumentException($"Object must be of type {nameof(BandUtil)}");
            }

            public BandUtil(int series, int index, double utilBound)
            {
                Series = series;
                Index = index;
                UtilBound = utilBound;
            }

            public int Series { get; }
            public int Index { get; }
            public double UtilBound { get; }
        }

        private readonly struct MmuBand
        {
            public MmuBand(double minUtil, double cumUtil, int pos)
            {
                MinUtil = minUtil;
                CumUtil = cumUtil;
                Pos = pos;
            }

            public double MinUtil { get; }
            public double CumUtil { get; }
            public int Pos { get; }
        }

        /// <summary>
        ///     mud is an updatable mutator utilization distribution.
        ///     This is a continuous distribution of duration over mutator
        ///     utilization. For example, the integral from mutator utilization a
        ///     to b is the total duration during which the mutator utilization was
        ///     in the range [a, b].
        ///     This distribution is *not* normalized (it is not a probability
        ///     distribution). This makes it easier to work with as it's being
        ///     updated.
        ///     It is represented as the sum of scaled uniform distribution
        ///     functions and Dirac delta functions (which are treated as
        ///     degenerate uniform distributions).
        /// </summary>
        private class Mud
        {
            private const int Degree = 1024;
            private readonly double[] _hist = new double[Degree];
            private List<Edge> _sorted = new List<Edge>();
            private int _trackBucket;
            private double _trackMass;
            private double _trackSum;
            private List<Edge> _unsorted = new List<Edge>();

            public double TrackMass
            {
                get => _trackMass;
                set
                {
                    _trackMass = value;

                    var sum = 0.0;
                    for (var i = 0; i < _hist.Length; i++)
                    {
                        var newSum = sum + _hist[i];
                        if (newSum > value)
                        {
                            _trackBucket = i;
                            _trackSum = sum;
                            return;
                        }

                        sum = newSum;
                    }

                    _trackBucket = _hist.Length;
                    _trackSum = sum;
                }
            }

            /// <summary>
            ///     add adds a uniform function over [l, r] scaled so the total weight
            ///     of the uniform is area. If l==r, this adds a Dirac delta function.
            /// </summary>
            public void Add(double l, double r, double area)
            {
                if (area == 0) return;
                if (r < l)
                {
                    var tmp = l;
                    l = r;
                    r = tmp;
                }

                if (Math.Abs(l - r) < 1e-5)
                {
                    _unsorted.Add(new Edge(l, 0, area));
                }
                else
                {
                    var delta = area / (r - l);
                    _unsorted.Add(new Edge(l, delta, 0));
                    _unsorted.Add(new Edge(r, -delta, 0));
                }

                var lb = (int) Math.Truncate(l * Degree);
                var lf = l * Degree - lb;
                if (lb >= Degree)
                {
                    lb = Degree - 1;
                    lf = 1;
                }

                if (Math.Abs(l - r) < 1e-5)
                {
                    _hist[lb] += area;
                }
                else
                {
                    var rb = (int) Math.Truncate(r * Degree);
                    var rf = r * Degree - rb;
                    if (rb >= Degree)
                    {
                        rb = Degree - 1;
                        rf = 1;
                    }

                    if (lb == rb)
                    {
                        _hist[lb] += area;
                    }
                    else
                    {
                        var perBucket = area / (r - l) / Degree;
                        _hist[lb] += perBucket * (1 - lf);
                        _hist[rb] += perBucket * rf;
                        for (var i = lb + 1; i < rb; i++) _hist[i] += perBucket;
                    }
                }

                var thresh = _trackBucket / Degree;
                if (l >= thresh) return;
                _trackSum += r < thresh ? area : area * (thresh - l) / (r - l);
                // The tracked mass now falls in a different bucket. Recompute the inverse cumulative sum.
                if (_trackSum >= _trackMass) TrackMass = _trackMass;
            }

            public bool ApproxInvCumulativeSum(out double lower, out double upper)
            {
                if (_trackBucket == _hist.Length)
                {
                    upper = lower = double.NaN;
                    return false;
                }

                lower = _trackBucket / (double) Degree;
                upper = (_trackBucket + 1) / (double) Degree;
                return true;
            }

            public double? InvCumulativeSum(double y)
            {
                if (_sorted.Count == 0 && _unsorted.Count == 0) return null;

                var edges = _unsorted;
                edges.Sort((i, j) => i.X.CompareTo(j.X));
                _unsorted = new List<Edge>();
                if (_sorted.Count == 0)
                {
                    _sorted = edges;
                }
                else
                {
                    var size = edges.Count + _sorted.Count;
                    var newSorted = new List<Edge>(size);
                    var oldSorted = _sorted;
                    int j = 0, i = 0;
                    for (var cnt = 0; cnt < size; cnt++)
                        if (i >= oldSorted.Count)
                        {
                            newSorted.Add(edges[j]);
                            j++;
                        }
                        else if (j >= edges.Count)
                        {
                            newSorted.Add(oldSorted[i]);
                            i++;
                        }
                        else if (oldSorted[i].X < edges[j].X)
                        {
                            newSorted.Add(oldSorted[i]);
                            i++;
                        }
                        else
                        {
                            newSorted.Add(edges[j]);
                            j++;
                        }

                    _sorted = newSorted;
                }

                double csum = .0, rate = .0;
                var x = .0;
                foreach (var e in _sorted)
                {
                    var newCsum = csum + (e.X - x) * rate;
                    if (newCsum >= y)
                    {
                        x = rate == 0 ? e.X : (y - csum) / rate + x;
                        break;
                    }

                    newCsum += e.Dirac;
                    x = e.X;
                    if (newCsum >= y) break;
                    csum = newCsum;
                    rate += e.Delta;
                }

                return x;
            }

            private readonly struct Edge
            {
                public Edge(double x, double delta, double dirac)
                {
                    X = x;
                    Delta = delta;
                    Dirac = dirac;
                }

                public double X { get; }
                public double Delta { get; }
                public double Dirac { get; }
            }
        }
    }
}