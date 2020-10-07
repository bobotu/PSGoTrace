using System.Management.Automation;
using PSGoTrace.Library;
using PSGoTrace.Library.Types;

namespace PSGoTrace
{
    [Cmdlet(VerbsCommon.Get, "TraceTasks")]
    [OutputType(typeof(UserTask))]
    public class GetTraceTasksCommand : PSCmdlet
    {
        [Parameter(Mandatory = true, Position = 0)]
        public TraceEvent[] TraceEvents { get; set; }

        protected override void ProcessRecord()
        {
            var goroutines = new GoroutinesStat(TraceEvents);
            var tasks = new UserTasksStat(TraceEvents, goroutines);

            foreach (var task in tasks.Values)
            {
                WriteObject(task);
            }
        }
    }
}