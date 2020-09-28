using System;
using System.Collections.Generic;
using System.IO;
using PSGoTrace.Library.Analyzer;
using TraceViewer.Trace.Analyzer;
using TraceViewer.Trace.Records;

namespace PSGoTrace.Library
{
    public class ProgramTrace
    {
        private readonly Lazy<AnnotationAnalyzer> _annotationAnalyzer;
        private readonly GcAnalyzer _gcAnalyzer;
        private readonly Lazy<GoroutinesAnalyzer> _goroutinesAnalyzer;

        private readonly Dictionary<GcAnalyzer.Option, MmuCurve> _mmuCurves =
            new Dictionary<GcAnalyzer.Option, MmuCurve>();

        private readonly Dictionary<GcAnalyzer.Option, IList<IList<MutatorUtil>>> _mus =
            new Dictionary<GcAnalyzer.Option, IList<IList<MutatorUtil>>>();

        private ProgramTrace(TraceRecords trace)
        {
            _goroutinesAnalyzer = new Lazy<GoroutinesAnalyzer>(() => new GoroutinesAnalyzer(trace.Events));
            _gcAnalyzer = new GcAnalyzer(trace);
            _annotationAnalyzer =
                new Lazy<AnnotationAnalyzer>(() => new AnnotationAnalyzer(trace, _goroutinesAnalyzer.Value));
        }

        public Goroutine GetGoroutine(ulong id)
        {
            return _goroutinesAnalyzer.Value.Goroutines[id];
        }

        public IList<IList<MutatorUtil>> GetMmu(GcAnalyzer.Option option)
        {
            return _mus.GetOrAdd(option, opt => _gcAnalyzer.MutatorUtilization(opt));
        }

        public MmuCurve GetMmuCurve(GcAnalyzer.Option option)
        {
            return _mmuCurves.GetOrAdd(option, opt => new MmuCurve(GetMmu(opt)));
        }

        public IReadOnlyList<UserRegion> GetUserRegions()
        {
            return _annotationAnalyzer.Value.Regions;
        }

        public UserTask GetUserTask(ulong id)
        {
            return _annotationAnalyzer.Value.Tasks[id];
        }

        public IEnumerable<UserTask> GetUserTasks()
        {
            return _annotationAnalyzer.Value.Tasks.Values;
        }

        public IReadOnlyList<TraceEvent> GetGcEvents()
        {
            return _annotationAnalyzer.Value.GcEvents;
        }

        public static ProgramTrace Load(Stream source)
        {
            var trace = TraceRecords.FromStream(source);
            return new ProgramTrace(trace);
        }
    }
}