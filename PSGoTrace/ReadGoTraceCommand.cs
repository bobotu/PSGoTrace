using System.Management.Automation;
using PSGoTrace.Library;
using PSGoTrace.Library.Types;

namespace PSGoTrace
{
    [Cmdlet(VerbsCommunications.Read, "GoTrace")]
    [OutputType(typeof(TraceEvent))]
    public class ReadGoTraceCommand : PSCmdlet
    {
        [Parameter(Mandatory = true, Position = 0)]
        public string Path { get; set; }

        protected override void ProcessRecord()
        {
            using var parser = new TraceParser(Path);
            foreach (var traceEvent in parser.Parse()) WriteObject(traceEvent);
        }
    }
}