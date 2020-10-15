using System.Management.Automation;
using PSGoTrace.Library;
using PSGoTrace.Library.Types;

namespace PSGoTrace
{
    [Cmdlet(VerbsCommon.Get, "TraceMutatorUtil")]
    [OutputType(typeof(MutatorUtilStat))]
    public class GetTraceMutatorUtilCommand : PSCmdlet
    {
        private bool _assist;
        private bool _background;
        private bool _perProc;
        private bool _stw;
        private bool _sweep;

        [Parameter(Mandatory = true, Position = 0)]
        public TraceEvent[] TraceEvents { get; set; }

        [Parameter]
        public SwitchParameter Stw
        {
            get => _stw;
            set => _stw = value;
        }

        [Parameter]
        public SwitchParameter Background
        {
            get => _background;
            set => _background = value;
        }

        [Parameter]
        public SwitchParameter Assist
        {
            get => _assist;
            set => _assist = value;
        }

        [Parameter]
        public SwitchParameter Sweep
        {
            get => _sweep;
            set => _sweep = value;
        }

        [Parameter]
        public SwitchParameter PerProc
        {
            get => _perProc;
            set => _perProc = value;
        }

        private const MutatorUtilStat.Option OptionAll = MutatorUtilStat.Option.Assist |
                                                         MutatorUtilStat.Option.Background |
                                                         MutatorUtilStat.Option.Stw | MutatorUtilStat.Option.Sweep;

        protected override void ProcessRecord()
        {
            MutatorUtilStat.Option option = 0;
            option = _stw ? option | MutatorUtilStat.Option.Stw : option;
            option = _background ? option | MutatorUtilStat.Option.Background : option;
            option = _assist ? option | MutatorUtilStat.Option.Assist : option;
            option = _sweep ? option | MutatorUtilStat.Option.Sweep : option;
            option = option == 0 ? OptionAll : option;
            option = _perProc ? option | MutatorUtilStat.Option.PerProc : option;

            WriteObject(new MutatorUtilStat(option, TraceEvents));
        }
    }
}