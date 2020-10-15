using System.Management.Automation;
using PSGoTrace.Library;
using PSGoTrace.Library.Types;

namespace PSGoTrace
{
    [Cmdlet(VerbsCommon.Get, "TraceGoroutines")]
    [OutputType(typeof(GoroutinesStat))]
    public class GetTraceGoroutinesCommand : PSCmdlet
    {
        [Parameter(Mandatory = true, Position = 0)]
        public TraceEvent[] TraceEvents { get; set; }

        protected override void ProcessRecord()
        {
            WriteObject(new GoroutinesStat(TraceEvents));
        }
    }
}