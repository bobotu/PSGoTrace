using System;
using System.Collections.Generic;
using System.Linq;

namespace PSGoTrace.Library.Types
{
    public class UserTask : IEquatable<UserTask>
    {
        private readonly List<UserTask> _children = new List<UserTask>();
        private readonly List<TraceEvent> _events = new List<TraceEvent>();
        private readonly long _worldEnd;

        private readonly long _worldStart;
        private List<UserRegion> _regions = new List<UserRegion>();

        internal UserTask(long worldStart, long worldEnd, ulong id)
        {
            _worldStart = worldStart;
            _worldEnd = worldEnd;
            Id = id;
            Name = "";
        }

        public ulong Id { get; }
        public string Name { get; private set; }
        public IReadOnlyList<TraceEvent> Events => _events;
        public IReadOnlyList<UserRegion> Regions => _regions;
        public ISet<ulong> Goroutines { get; } = new HashSet<ulong>();
        public TraceEvent? Create { get; private set; }
        public TraceEvent? End { get; private set; }

        public UserTask? Parent { get; private set; }
        public IReadOnlyList<UserTask>? Children => _children.Count > 0 ? _children : null;

        /// <summary>
        ///     Is true only if both start and end events of this task are present in the trace.
        /// </summary>
        public bool Completed => Create != null && End != null;

        /// <summary>
        ///     The timestamp of first event.
        /// </summary>
        public long FirstTimestamp => Create?.Ts ?? _worldStart;

        /// <summary>
        ///     The timestamp of last event or end of task.
        /// </summary>
        public long LastTimestamp => LastEvent?.Ts ?? EndTimestamp;

        /// <summary>
        ///     The timestamp when task finished.
        /// </summary>
        public long EndTimestamp => End?.Ts ?? _worldEnd;

        /// <summary>
        ///     The last event in task.
        /// </summary>
        public TraceEvent? LastEvent => Events.Count > 0 ? Events[^1] : null;

        public long Duration => EndTimestamp - FirstTimestamp;

        public IList<UserTask> Descendants
        {
            get
            {
                var res = new List<UserTask> {this};
                for (var i = 0; i < res.Count; i++) res.AddRange(res[i].Children ?? Enumerable.Empty<UserTask>());
                return res;
            }
        }

        public bool Equals(UserTask? other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Id == other.Id;
        }

        /// <summary>
        ///     overlappingInstant reports whether the instantaneous event, ev, occurred during
        ///     any of the task's region if ev is a goroutine-local event, or overlaps with the
        ///     task's lifetime if ev is a global event.
        /// </summary>
        public bool OverlappingInstant(TraceEvent ev)
        {
            var isUserAnnotation = ev.Type == EventType.UserLog || ev.Type == EventType.UserRegion ||
                                   ev.Type == EventType.UserTaskCreate || ev.Type == EventType.UserTaskEnd;
            if (isUserAnnotation && Id != ev.Args[0]) return false;
            if (ev.Ts < FirstTimestamp || EndTimestamp < ev.Ts) return false;
            if (ev.P == (int) ProcIdentifier.Gc) return true;

            return Regions.Where(r => r.Creator == ev.G).Any(r =>
            {
                var regionStart = r.Start?.Ts ?? _worldStart;
                var regionEnd = r.End?.Ts ?? _worldEnd;
                return regionStart <= ev.Ts && ev.Ts <= regionEnd;
            });
        }

        /// <summary>
        ///     overlappingDuration reports whether the durational event, ev, overlaps with
        ///     any of the task's region if ev is a goroutine-local event, or overlaps with
        ///     the task's lifetime if ev is a global event. It returns the overlapping time
        ///     as well.
        /// </summary>
        public long OverlappingDuration(TraceEvent ev)
        {
            var start = ev.Ts;
            var end = ev.Link?.Ts ?? LastTimestamp;
            if (start > end) return 0;

            var id1 = ev.G;
            var id2 = ev.Link?.G ?? ev.G;

            if (ev.P == (int) ProcIdentifier.Gc)
                return OverlappingDuration(FirstTimestamp, EndTimestamp, start, end);

            var overlapping = 0L;
            var lasRegionEnd = 0L;
            foreach (var region in Regions.Where(r => r.Creator == id1 || r.Creator == id2))
            {
                var regionStart = region.Start?.Ts ?? _worldStart;
                var regionEnd = region.End?.Ts ?? _worldEnd;
                if (regionStart < lasRegionEnd) continue;
                var o = OverlappingDuration(regionStart, regionEnd, start, end);
                if (o <= 0) continue;
                lasRegionEnd = regionEnd;
                overlapping += o;
            }

            return overlapping;
        }

        /// <summary>
        ///     RelatedGoroutines returns IDs of goroutines related to the task. A goroutine
        ///     is related to the task if user annotation activities for the task occurred.
        ///     If non-zero depth is provided, this searches all events with BFS and includes
        ///     goroutines unblocked any of related goroutines to the result.
        /// </summary>
        /// <param name="events">event list</param>
        /// <param name="depth">search depth</param>
        /// <returns>set of goroutine ids</returns>
        public ISet<ulong> RelatedGoroutines(IList<TraceEvent> events, int depth)
        {
            var set = new HashSet<ulong>(Goroutines);

            for (var i = 0; i < depth; i++)
            {
                var added = events.Where(ev =>
                        !(ev.Ts < FirstTimestamp || ev.Ts > EndTimestamp) &&
                        ev.Type == EventType.GoUnblock && set.Contains(ev.Args[0]))
                    .Select(ev => ev.G);
                set.UnionWith(added);
            }

            return set;
        }

        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((UserTask) obj);
        }

        public override int GetHashCode() => Id.GetHashCode();

        public static bool operator ==(UserTask? left, UserTask? right) => Equals(left, right);

        public static bool operator !=(UserTask? left, UserTask? right) => !Equals(left, right);

        internal void AddGoroutine(ulong id)
        {
            Goroutines.Add(id);
        }

        internal void AddRegion(UserRegion region)
        {
            _regions.Add(region);
        }

        internal void AddEvent(TraceEvent ev)
        {
            _events.Add(ev);
            Goroutines.Add(ev.G);

            switch (ev.Type)
            {
                case EventType.UserTaskCreate:
                    Name = ev.StringArgs![0];
                    Create = ev;
                    break;
                case EventType.UserTaskEnd:
                    End = ev;
                    break;
            }
        }

        internal void AddChild(UserTask child)
        {
            _children.Add(child);
            child.Parent = this;
        }

        internal void SortRegions()
        {
            _regions = _regions.OrderBy(r => (r.Start?.Ts ?? _worldStart, r.End?.Ts ?? _worldEnd)).ToList();
        }

        /// <summary>
        ///     overlappingDuration returns the overlapping time duration between
        ///     two time intervals [start1, end1] and [start2, end2] where
        ///     start, end parameters are all int64 representing nanoseconds.
        /// </summary>
        /// <param name="start1">start of first time span</param>
        /// <param name="end1">end of first time span</param>
        /// <param name="start2">start of second time span</param>
        /// <param name="end2">end of second time span</param>
        /// <returns>overlapping time duration between two time intervals</returns>
        private static long OverlappingDuration(long start1, long end1, long start2, long end2) =>
            end1 < start2 || end2 < start1 ? 0 : Math.Min(end1, end2) - Math.Max(start1, start2);
    }
}