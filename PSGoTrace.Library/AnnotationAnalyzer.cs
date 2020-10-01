using System;
using System.Collections.Generic;
using System.Linq;
using PSGoTrace.Library.Types;

namespace PSGoTrace.Library
{
    public class AnnotationAnalyzer
    {
        public AnnotationAnalyzer(IList<TraceEvent> events, GoroutinesAnalyzer goroutinesAnalyzer)
        {
            if (events.Count == 0) throw new ArgumentException("trace records is empty");

            var firstTimestamp = events[0].Ts;
            var lastTimestamp = events[^1].Ts;
            var tasks = new Dictionary<ulong, UserTask>();
            var regions = new List<UserRegion>();
            var gc = new List<TraceEvent>();

            UserTask CreateTask(ulong id)
            {
                return new UserTask(firstTimestamp, lastTimestamp, id);
            }

            foreach (var ev in events)
                switch (ev.Type)
                {
                    case EventType.UserTaskCreate:
                    case EventType.UserTaskEnd:
                    case EventType.UserLog:
                        var id = ev.Args[0];
                        var task = tasks.GetOrAdd(id, CreateTask);
                        task.AddEvent(ev);
                        if (ev.Type == EventType.UserTaskCreate && ev.Args[1] != 0)
                            tasks.GetOrAdd(ev.Args[1], CreateTask).AddChild(task);
                        break;
                    case EventType.GcStart:
                        gc.Add(ev);
                        break;
                }

            var gs = goroutinesAnalyzer.Goroutines;
            foreach (var (id, stats) in gs)
            foreach (var region in stats.Regions)
            {
                if (region.TaskId != 0)
                {
                    var task = tasks.GetOrAdd(region.TaskId, CreateTask);
                    task.AddGoroutine(id);
                    task.AddRegion(region);
                }

                regions.Add(region);
            }

            tasks.Values.AsParallel().ForEach(t => t.SortRegions());

            Tasks = tasks;
            Regions = regions;
            GcEvents = gc;
        }

        public IDictionary<ulong, UserTask> Tasks { get; }
        public IList<UserRegion> Regions { get; }
        public IList<TraceEvent> GcEvents { get; }
    }
}