namespace PSGoTrace.Library.Types
{
    public enum EventType
    {
        /// <summary>
        ///     unused
        /// </summary>
        None,

        /// <summary>
        ///     start of per-P batch of events [pid, timestamp]
        /// </summary>
        Batch,

        /// <summary>
        ///     contains tracer timer frequency [frequency (ticks per second)]
        /// </summary>
        Frequency,

        /// <summary>
        ///     stack [stack id, number of PCs, array of {PC, func string ID, file string ID, line}]
        /// </summary>
        Stack,

        /// <summary>
        ///     current value of GOMAXPROCS [timestamp, GOMAXPROCS, stack id]
        /// </summary>
        Gomaxprocs,

        /// <summary>
        ///     start of P [timestamp, thread id]
        /// </summary>
        ProcStart,

        /// <summary>
        ///     stop of P [timestamp]
        /// </summary>
        ProcStop,

        /// <summary>
        ///     GC start [timestamp, seq, stack id]
        /// </summary>
        GcStart,

        /// <summary>
        ///     GC done [timestamp]
        /// </summary>
        GcDone,

        /// <summary>
        ///     GC mark termination start [timestamp, kind]
        /// </summary>
        GcStwStart,

        /// <summary>
        ///     GC mark termination done [timestamp]
        /// </summary>
        GcStwDone,

        /// <summary>
        ///     GC sweep start [timestamp, stack id]
        /// </summary>
        GcSweepStart,

        /// <summary>
        ///     GC sweep done [timestamp, swept, reclaimed]
        /// </summary>
        GcSweepDone,

        /// <summary>
        ///     goroutine creation [timestamp, new goroutine id, new stack id, stack id]
        /// </summary>
        GoCreate,

        /// <summary>
        ///     goroutine starts running [timestamp, goroutine id, seq]
        /// </summary>
        GoStart,

        /// <summary>
        ///     goroutine ends [timestamp]
        /// </summary>
        GoEnd,

        /// <summary>
        ///     goroutine stops (like in select{}) [timestamp, stack]
        /// </summary>
        GoStop,

        /// <summary>
        ///     goroutine calls Gosched [timestamp, stack]
        /// </summary>
        GoSched,

        /// <summary>
        ///     goroutine is preempted [timestamp, stack]
        /// </summary>
        GoPreempt,

        /// <summary>
        ///     goroutine calls Sleep [timestamp, stack]
        /// </summary>
        GoSleep,

        /// <summary>
        ///     goroutine blocks [timestamp, stack]
        /// </summary>
        GoBlock,

        /// <summary>
        ///     goroutine is unblocked [timestamp, goroutine id, seq, stack]
        /// </summary>
        GoUnblock,

        /// <summary>
        ///     goroutine blocks on chan send [timestamp, stack]
        /// </summary>
        GoBlockSend,

        /// <summary>
        ///     goroutine blocks on chan recv [timestamp, stack]
        /// </summary>
        GoBlockRecv,

        /// <summary>
        ///     goroutine blocks on select [timestamp, stack]
        /// </summary>
        GoBlockSelect,

        /// <summary>
        ///     goroutine blocks on Mutex/RWMutex [timestamp, stack]
        /// </summary>
        GoBlockSync,

        /// <summary>
        ///     goroutine blocks on Cond [timestamp, stack]
        /// </summary>
        GoBlockCond,

        /// <summary>
        ///     goroutine blocks on network [timestamp, stack]
        /// </summary>
        GoBlockNet,

        /// <summary>
        ///     syscall enter [timestamp, stack]
        /// </summary>
        GoSysCall,

        /// <summary>
        ///     syscall exit [timestamp, goroutine id, seq, real timestamp]
        /// </summary>
        GoSysExit,

        /// <summary>
        ///     syscall blocks [timestamp]
        /// </summary>
        GoSysBlock,

        /// <summary>
        ///     denotes that goroutine is blocked when tracing starts [timestamp, goroutine id]
        /// </summary>
        GoWaiting,

        /// <summary>
        ///     denotes that goroutine is in syscall when tracing starts [timestamp, goroutine id]
        /// </summary>
        GoInSyscall,

        /// <summary>
        ///     memstats.heap_live change [timestamp, heap_alloc]
        /// </summary>
        HeapAlloc,

        /// <summary>
        ///     memstats.next_gc change [timestamp, next_gc]
        /// </summary>
        NextGc,

        /// <summary>
        ///     denotes timer goroutine [timer goroutine id]
        /// </summary>
        TimerGoroutine,

        /// <summary>
        ///     denotes that the previous wakeup of this goroutine was futile [timestamp]
        /// </summary>
        FutileWakeup,

        /// <summary>
        ///     string dictionary entry [ID, length, string]
        /// </summary>
        String,

        /// <summary>
        ///     goroutine starts running on the same P as the last event [timestamp, goroutine id]
        /// </summary>
        GoStartLocal,

        /// <summary>
        ///     goroutine is unblocked on the same P as the last event [timestamp, goroutine id, stack]
        /// </summary>
        GoUnblockLocal,

        /// <summary>
        ///     syscall exit on the same P as the last event [timestamp, goroutine id, real timestamp]
        /// </summary>
        GoSysExitLocal,

        /// <summary>
        ///     goroutine starts running with label [timestamp, goroutine id, seq, label string id]
        /// </summary>
        GoStartLabel,

        /// <summary>
        ///     goroutine blocks on GC assist [timestamp, stack]
        /// </summary>
        GoBlockGc,

        /// <summary>
        ///     GC mark assist start [timestamp, stack]
        /// </summary>
        GcMarkAssistStart,

        /// <summary>
        ///     GC mark assist done [timestamp]
        /// </summary>
        GcMarkAssistDone,

        /// <summary>
        ///     trace.NewContext [timestamp, internal task id, internal parent id, stack, name string]
        /// </summary>
        UserTaskCreate,

        /// <summary>
        ///     end of task [timestamp, internal task id, stack]
        /// </summary>
        UserTaskEnd,

        /// <summary>
        ///     trace.WithRegion [timestamp, internal task id, mode(0:start, 1:end), stack, name string]
        /// </summary>
        UserRegion,

        /// <summary>
        ///     trace.Log [timestamp, internal id, key string id, stack, value string]
        /// </summary>
        UserLog
    }
}