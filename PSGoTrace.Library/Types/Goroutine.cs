using System.Collections.Generic;

namespace PSGoTrace.Library.Types
{
    public class Goroutine
    {
        internal Goroutine(ulong id, string name, ulong pc, long creationTime, long startTime, long? endTime,
            IReadOnlyList<UserRegion> regions, ExecutionStat stat)
        {
            Id = id;
            Name = name;
            Pc = pc;
            CreationTime = creationTime;
            StartTime = startTime;
            EndTime = endTime;
            Regions = regions;
            Stat = stat;
        }

        public ulong Id { get; }
        public string Name { get; }
        public ulong Pc { get; }
        public long CreationTime { get; }
        public long StartTime { get; }
        public long? EndTime { get; }

        public IReadOnlyList<UserRegion> Regions { get; }

        public ExecutionStat Stat { get; }
    }
}