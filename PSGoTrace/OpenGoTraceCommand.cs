using System;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using PSGoTrace.Library;
using PSGoTrace.Library.Parser;

namespace PSGoTrace
{
    [Cmdlet(VerbsCommon.Open, "GoTrace")]
    [OutputType(typeof(TraceEvent))]
    public class OpenGoTraceCommand : PSCmdlet
    {
        [Parameter(
            Mandatory = true,
            Position = 0,
            ValueFromPipeline = true,
            ValueFromPipelineByPropertyName = true)]
        public string Path { get; set; }

        protected override void ProcessRecord()
        {
            using var parser = new TraceParser(Path);
            var events = parser.Parse();
            foreach (var traceEvent in events)
            {
                WriteObject(traceEvent);
            }
        }
    }
}