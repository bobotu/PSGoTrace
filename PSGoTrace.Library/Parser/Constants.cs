namespace PSGoTrace.Library.Parser
{
    internal enum GStatus
    {
        Dead,
        Runnable,
        Running,
        Waiting
    }

    internal enum ProcIdentifier
    {
        Fake = 1000000,
        Timer = 1000001,
        NetPoll = 1000002,
        Syscall = 1000003,
        Gc = 1000004
    }
}