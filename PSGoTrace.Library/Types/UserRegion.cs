namespace PSGoTrace.Library.Types
{
    public class UserRegion
    {
        internal UserRegion(ulong taskId, ulong creator,
            string name = "", TraceEvent? start = null, ExecutionStat stat = default)
        {
            TaskId = taskId;
            Creator = creator;
            Name = name;
            Start = start;
            Stat = stat;
        }

        public ulong TaskId { get; }
        public ulong Creator { get; }
        public string Name { get; }
        public TraceEvent? Start { get; }
        public TraceEvent? End { get; internal set; }
        public StackFrame? Frame => Start?.Stack?[0];
        public ExecutionStat Stat { get; internal set; }
    }
}