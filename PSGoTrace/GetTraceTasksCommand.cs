using System;
using System.Management.Automation;
using PSGoTrace.Library;
using PSGoTrace.Library.Types;

namespace PSGoTrace
{
    [Cmdlet(VerbsCommon.Get, "TraceTasks")]
    [OutputType(typeof(UserTasksStat))]
    public class GetTraceTasksCommand : PSCmdlet
    {
        [Parameter(Mandatory = true, Position = 0)]
        public TraceEvent[] TraceEvents { get; set; }

        [Parameter(Mandatory = false)] public GoroutinesStat GoroutinesStat { get; set; }

        protected override void ProcessRecord()
        {
            var tasks = new UserTasksStat(TraceEvents, GoroutinesStat ?? new GoroutinesStat(TraceEvents));
            WriteObject(tasks);
        }
    }
}