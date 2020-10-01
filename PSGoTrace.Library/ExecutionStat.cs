namespace PSGoTrace.Library
{
    public struct ExecutionStat
    {
        public long ExecTime { get; internal set; }
        public long SchedWaitTime { get; internal set; }
        public long IoTime { get; internal set; }
        public long BlockTime { get; internal set; }
        public long SyscallTime { get; internal set; }
        public long GcTime { get; internal set; }
        public long SweepTime { get; internal set; }
        public long AssitMarkTime { get; internal set; }
        public long TotalTime { get; internal set; }

        public ExecutionStat(long execTime, long schedWaitTime, long ioTime, long blockTime, long syscallTime,
            long gcTime, long sweepTime, long assitMarkTime, long totalTime)
        {
            ExecTime = execTime;
            SchedWaitTime = schedWaitTime;
            IoTime = ioTime;
            BlockTime = blockTime;
            SyscallTime = syscallTime;
            GcTime = gcTime;
            SweepTime = sweepTime;
            AssitMarkTime = assitMarkTime;
            TotalTime = totalTime;
        }

        public static ExecutionStat operator -(ExecutionStat lhs, ExecutionStat rhs)
        {
            return new ExecutionStat(
                    lhs.ExecTime - rhs.ExecTime,
                    lhs.SchedWaitTime - rhs.SchedWaitTime,
                    lhs.IoTime - rhs.IoTime,
                    lhs.BlockTime - rhs.BlockTime,
                    lhs.SyscallTime - rhs.SyscallTime,
                    lhs.GcTime - rhs.GcTime,
                    lhs.SweepTime - rhs.SweepTime, 
                lhs.AssitMarkTime - rhs.AssitMarkTime,
                lhs.TotalTime - rhs.TotalTime);
        }
    }
}