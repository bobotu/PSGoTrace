using System.Management.Automation;
using PSGoTrace.Library.Types;

namespace PSGoTrace
{
    [Cmdlet(VerbsCommon.Get, "TraceTasks")]
    [OutputType(typeof(int))]
    public class GetTraceTasksCommand : PSCmdlet
    {
        [Parameter(Mandatory = true,
            Position = 0,
            ValueFromPipeline = true,
            ValueFromPipelineByPropertyName = true)]
        public TraceEvent[] TraceEvents { get; set; }

        protected override void ProcessRecord()
        {
            WriteObject(TraceEvents.Length);
        }
    }
}