﻿using System;
using System.Collections.Generic;
using System.Linq;
using PSGoTrace.Library.Records;
using TraceViewer.Trace.Analyzer;
using TraceViewer.Trace.Records;

namespace PSGoTrace.Library.Analyzer
{
    public class AnnotationAnalyzer
    {
        public AnnotationAnalyzer(TraceRecords trace, GoroutinesAnalyzer goroutinesAnalyzer)
        {
            if (trace.Events.Count == 0) throw new ArgumentException("trace records is empty");
            var trace1 = trace;

            var firstTimestamp = trace1.Events[0].Ts;
            var lastTimestamp = trace1.Events[^1].Ts;
            var tasks = new Dictionary<ulong, UserTask>();
            var regions = new List<UserRegion>();
            var gc = new List<TraceEvent>();

            UserTask CreateTask(ulong id)
            {
                return new UserTask(firstTimestamp, lastTimestamp, id);
            }

            foreach (var ev in trace1.Events)
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

        public IReadOnlyDictionary<ulong, UserTask> Tasks { get; }
        public IReadOnlyList<UserRegion> Regions { get; }
        public IReadOnlyList<TraceEvent> GcEvents { get; }
    }
}