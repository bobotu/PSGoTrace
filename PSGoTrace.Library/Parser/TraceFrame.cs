﻿using System;
using System.Text;

namespace PSGoTrace.Library.Parser
{
    public readonly struct TraceFrame : IEquatable<TraceFrame>
    {
        public TraceFrame(ulong pc, string fn = "", string file = "", int line = default)
        {
            Pc = pc;
            Fn = fn;
            File = file;
            Line = line;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine(Fn);
            sb.Append($"\t{File}:{Line}");
            return sb.ToString();
        }

        public ulong Pc { get; }
        public string Fn { get; }
        public string File { get; }
        public int Line { get; }

        public bool Equals(TraceFrame other)
        {
            return Pc == other.Pc && Fn == other.Fn && File == other.File && Line == other.Line;
        }

        public override bool Equals(object? obj)
        {
            return obj is TraceFrame other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Pc, Fn, File, Line);
        }

        public static bool operator ==(TraceFrame left, TraceFrame right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(TraceFrame left, TraceFrame right)
        {
            return !left.Equals(right);
        }
    }
}