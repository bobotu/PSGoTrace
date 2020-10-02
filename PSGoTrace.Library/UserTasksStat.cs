using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using PSGoTrace.Library.Types;

namespace PSGoTrace.Library
{
    public class UserTasksStat : IReadOnlyDictionary<ulong, UserTask>
    {
        private readonly List<UserRegion> _regions = new List<UserRegion>();
        private readonly Dictionary<ulong, UserTask> _tasks = new Dictionary<ulong, UserTask>();

        public UserTasksStat(IList<TraceEvent> events, GoroutinesStat goroutines)
        {
            if (events.Count == 0) throw new ArgumentException("trace records is empty");

            var firstTimestamp = events[0].Ts;
            var lastTimestamp = events[^1].Ts;

            UserTask CreateTask(ulong id) => new UserTask(firstTimestamp, lastTimestamp, id);

            foreach (var ev in events)
                switch (ev.Type)
                {
                    case EventType.UserTaskCreate:
                    case EventType.UserTaskEnd:
                    case EventType.UserLog:
                        var id = ev.Args[0];
                        var task = _tasks.GetOrAdd(id, CreateTask);
                        task.AddEvent(ev);
                        if (ev.Type == EventType.UserTaskCreate && ev.Args[1] != 0)
                            _tasks.GetOrAdd(ev.Args[1], CreateTask).AddChild(task);
                        break;
                }

            foreach (var (id, stat) in goroutines)
            {
                foreach (var region in stat.Regions)
                {
                    if (region.TaskId != 0)
                    {
                        var task = _tasks.GetOrAdd(region.TaskId, CreateTask);
                        task.AddGoroutine(id);
                        task.AddRegion(region);
                    }

                    _regions.Add(region);
                }

                _tasks.Values.AsParallel().ForEach(t => t.SortRegions());
            }
        }

        public IReadOnlyList<UserRegion> Regions => _regions;
        public IEnumerator<KeyValuePair<ulong, UserTask>> GetEnumerator() => _tasks.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable) _tasks).GetEnumerator();

        public int Count => _tasks.Count;

        public bool ContainsKey(ulong key) => _tasks.ContainsKey(key);

        public bool TryGetValue(ulong key, out UserTask value) => _tasks.TryGetValue(key, out value);

        public UserTask this[ulong key] => _tasks[key];

        public IEnumerable<ulong> Keys => _tasks.Keys;

        public IEnumerable<UserTask> Values => _tasks.Values;
    }
}