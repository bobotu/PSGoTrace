using System;

namespace PSGoTrace.Library.Parser
{
    public sealed class EmptyTraceException : Exception
    {
    }

    public sealed class InvalidTraceException : Exception
    {
        public InvalidTraceException(string message) : base(message)
        {
        }
    }

    public sealed class TimeOrderException : Exception
    {
        public TimeOrderException() : base("time stamps out of order")
        {
        }
    }

    public sealed class InvalidEventException : Exception
    {
        public InvalidEventException(TraceEvent ev, string message = "") : base(
            $"p {ev.P} g {ev.G} {message} (offset {ev.Offset}, time {ev.Ts})")
        {
        }
    }
}