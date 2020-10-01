using System;
using System.Collections.Generic;

namespace PSGoTrace.Library.Types
{
    public class TraceEvent : IEquatable<TraceEvent>
    {
        public TraceEvent(int offset, EventType type, int p, ulong g)
        {
            Offset = offset;
            Type = type;
            P = p;
            G = g;
            Args = new ulong[3];
        }

        public int Offset { get; }
        public EventType Type { get; internal set; }
        public long Ts { get; internal set; }
        public long Seq { get; internal set; }
        public int P { get; internal set; }
        public ulong G { get; internal set; }
        public ulong StackId { get; internal set; }
        public StackTrace? Stack { get; internal set; }
        public ulong[] Args { get; }
        public string[]? StringArgs { get; internal set; }
        public TraceEvent? Link { get; internal set; }

        public static IComparer<TraceEvent> SeqComparer { get; } = new SeqRelationalComparer();

        public static IComparer<TraceEvent> TsComparer { get; } = new TsRelationalComparer();

        public bool Equals(TraceEvent? other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Offset == other.Offset;
        }

        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((TraceEvent) obj);
        }

        public override int GetHashCode() => Offset;

        public static bool operator ==(TraceEvent? left, TraceEvent? right) => Equals(left, right);

        public static bool operator !=(TraceEvent? left, TraceEvent? right) => !Equals(left, right);

        private sealed class SeqRelationalComparer : IComparer<TraceEvent>
        {
            public int Compare(TraceEvent? x, TraceEvent? y)
            {
                if (ReferenceEquals(x, y)) return 0;
                if (ReferenceEquals(null, y)) return 1;
                if (ReferenceEquals(null, x)) return -1;
                return x.Seq.CompareTo(y.Seq);
            }
        }

        private sealed class TsRelationalComparer : IComparer<TraceEvent>
        {
            public int Compare(TraceEvent? x, TraceEvent? y)
            {
                if (ReferenceEquals(x, y)) return 0;
                if (ReferenceEquals(null, y)) return 1;
                if (ReferenceEquals(null, x)) return -1;
                return x.Ts.CompareTo(y.Ts);
            }
        }
    }
}