using System;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using PSGoTrace.Library;

namespace PSGoTrace.Cmdlet
{
    [Cmdlet(VerbsCommon.Open, "GoTrace")]
    [OutputType(typeof(ProgramTrace))]
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
            using var file = File.OpenRead(Path);
            WriteObject(ProgramTrace.Load(file));
        }
    }
}