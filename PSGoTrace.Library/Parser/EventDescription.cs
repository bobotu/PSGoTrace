using System.Collections.Generic;

namespace PSGoTrace.Library.Parser
{
    public sealed class EventDescription
    {
        private static readonly Dictionary<EventType, EventDescription> Descriptions =
            new Dictionary<EventType, EventDescription>
            {
                {EventType.None, new EventDescription("None", 1005, false, new string[] { }, null)},
                {EventType.Batch, new EventDescription("Batch", 1005, false, new[] {"p", "ticks"}, null)},
                {EventType.Frequency, new EventDescription("Frequency", 1005, false, new[] {"freq"}, null)},
                {EventType.Stack, new EventDescription("Stack", 1005, false, new[] {"id", "siz"}, null)},
                {EventType.Gomaxprocs, new EventDescription("Gomaxprocs", 1005, true, new[] {"procs"}, null)},
                {EventType.ProcStart, new EventDescription("ProcStart", 1005, false, new[] {"thread"}, null)},
                {EventType.ProcStop, new EventDescription("ProcStop", 1005, false, new string[] { }, null)},
                {EventType.GcStart, new EventDescription("GCStart", 1005, true, new[] {"seq"}, null)},
                {EventType.GcDone, new EventDescription("GCDone", 1005, false, new string[] { }, null)},
                {
                    EventType.GcStwStart,
                    new EventDescription("GCSTWStart", 1005, false, new[] {"kindid"}, new[] {"kind"})
                },
                {EventType.GcStwDone, new EventDescription("GCSTWDone", 1005, false, new string[] { }, null)},
                {EventType.GcSweepStart, new EventDescription("GCSweepStart", 1005, true, new string[] { }, null)},
                {
                    EventType.GcSweepDone,
                    new EventDescription("GCSweepDone", 1005, false, new[] {"swept", "reclaimed"}, null)
                },
                {EventType.GoCreate, new EventDescription("GoCreate", 1005, true, new[] {"g", "stack"}, null)},
                {EventType.GoStart, new EventDescription("GoStart", 1005, false, new[] {"g", "seq"}, null)},
                {EventType.GoEnd, new EventDescription("GoEnd", 1005, false, new string[] { }, null)},
                {EventType.GoStop, new EventDescription("GoStop", 1005, true, new string[] { }, null)},
                {EventType.GoSched, new EventDescription("GoSched", 1005, true, new string[] { }, null)},
                {EventType.GoPreempt, new EventDescription("GoPreempt", 1005, true, new string[] { }, null)},
                {EventType.GoSleep, new EventDescription("GoSleep", 1005, true, new string[] { }, null)},
                {EventType.GoBlock, new EventDescription("GoBlock", 1005, true, new string[] { }, null)},
                {EventType.GoUnblock, new EventDescription("GoUnblock", 1005, true, new[] {"g", "seq"}, null)},
                {EventType.GoBlockSend, new EventDescription("GoBlockSend", 1005, true, new string[] { }, null)},
                {EventType.GoBlockRecv, new EventDescription("GoBlockRecv", 1005, true, new string[] { }, null)},
                {EventType.GoBlockSelect, new EventDescription("GoBlockSelect", 1005, true, new string[] { }, null)},
                {EventType.GoBlockSync, new EventDescription("GoBlockSync", 1005, true, new string[] { }, null)},
                {EventType.GoBlockCond, new EventDescription("GoBlockCond", 1005, true, new string[] { }, null)},
                {EventType.GoBlockNet, new EventDescription("GoBlockNet", 1005, true, new string[] { }, null)},
                {EventType.GoSysCall, new EventDescription("GoSysCall", 1005, true, new string[] { }, null)},
                {
                    EventType.GoSysExit,
                    new EventDescription("GoSysExit", 1005, false, new[] {"g", "seq", "ts"}, null)
                },
                {EventType.GoSysBlock, new EventDescription("GoSysBlock", 1005, false, new string[] { }, null)},
                {EventType.GoWaiting, new EventDescription("GoWaiting", 1005, false, new[] {"g"}, null)},
                {EventType.GoInSyscall, new EventDescription("GoInSyscall", 1005, false, new[] {"g"}, null)},
                {EventType.HeapAlloc, new EventDescription("HeapAlloc", 1005, false, new[] {"mem"}, null)},
                {EventType.NextGc, new EventDescription("NextGC", 1005, false, new[] {"mem"}, null)},
                {
                    EventType.TimerGoroutine,
                    new EventDescription("TimerGoroutine", 1005, false, new[] {"g"}, null)
                },
                {EventType.FutileWakeup, new EventDescription("FutileWakeup", 1005, false, new string[] { }, null)},
                {EventType.String, new EventDescription("String", 1007, false, new string[] { }, null)},
                {EventType.GoStartLocal, new EventDescription("GoStartLocal", 1007, false, new[] {"g"}, null)},
                {
                    EventType.GoUnblockLocal,
                    new EventDescription("GoUnblockLocal", 1007, true, new[] {"g"}, null)
                },
                {
                    EventType.GoSysExitLocal,
                    new EventDescription("GoSysExitLocal", 1007, false, new[] {"g", "ts"}, null)
                },
                {
                    EventType.GoStartLabel,
                    new EventDescription("GoStartLabel", 1008, false, new[] {"g", "seq", "labelid"},
                        new[] {"label"})
                },
                {EventType.GoBlockGc, new EventDescription("GoBlockGC", 1008, true, new string[] { }, null)},
                {
                    EventType.GcMarkAssistStart,
                    new EventDescription("GCMarkAssistStart", 1009, true, new string[] { }, null)
                },
                {
                    EventType.GcMarkAssistDone,
                    new EventDescription("GCMarkAssistDone", 1009, false, new string[] { }, null)
                },
                {
                    EventType.UserTaskCreate,
                    new EventDescription("UserTaskCreate", 1011, true, new[] {"taskid", "pid", "typeid"},
                        new[] {"name"})
                },
                {EventType.UserTaskEnd, new EventDescription("UserTaskEnd", 1011, true, new[] {"taskid"}, null)},
                {
                    EventType.UserRegion,
                    new EventDescription("UserRegion", 1011, true, new[] {"taskid", "mode", "typeid"},
                        new[] {"name"})
                },
                {
                    EventType.UserLog,
                    new EventDescription("UserLog", 1011, true, new[] {"id", "keyid"},
                        new[] {"category", "message"})
                }
            };

        private EventDescription(string name, int minVersion, bool stack, string[] args, string[]? stringArgs)
        {
            Name = name;
            MinVersion = minVersion;
            Stack = stack;
            Args = args;
            StringArgs = stringArgs;
        }

        public string Name { get; }
        internal int MinVersion { get; }
        public bool Stack { get; }
        public string[] Args { get; }
        public string[]? StringArgs { get; }

        public static EventDescription Of(EventType type)
        {
            return Descriptions[type];
        }
    }
}