using System;

namespace PSGoTrace.Library.Types
{
    public readonly struct StackFrame : IEquatable<StackFrame>
    {
        public StackFrame(ulong pc, string fn = "", string file = "", int line = default)
        {
            Pc = pc;
            Fn = fn;
            File = file;
            Line = line;
        }

        public override string ToString() => Fn;

        public ulong Pc { get; }
        public string Fn { get; }
        public string File { get; }
        public int Line { get; }

        public bool Equals(StackFrame other) =>
            Pc == other.Pc && Fn == other.Fn && File == other.File && Line == other.Line;

        public override bool Equals(object? obj) => obj is StackFrame other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Pc, Fn, File, Line);

        public static bool operator ==(StackFrame left, StackFrame right) => left.Equals(right);

        public static bool operator !=(StackFrame left, StackFrame right) => !left.Equals(right);
    }
}