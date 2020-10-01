using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace PSGoTrace.Library.Types
{
    public class StackTrace : IReadOnlyList<StackFrame>
    {
        private readonly List<StackFrame> _frames;

        internal StackTrace()
        {
            _frames = new List<StackFrame>();
        }

        internal StackTrace(int capacity)
        {
            _frames = new List<StackFrame>(capacity);
        }

        public IEnumerator<StackFrame> GetEnumerator() => _frames.GetEnumerator();


        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable) _frames).GetEnumerator();

        public int Count => _frames.Count;

        public StackFrame this[int index] => _frames[index];

        internal void Add(StackFrame frame)
        {
            _frames.Add(frame);
        }


        public override string ToString()
        {
            var sb = new StringBuilder();
            foreach (var frame in _frames)
            {
                sb.AppendLine(frame.Fn);
                sb.AppendLine($"{frame.File}:{frame.Line}");
            }

            return sb.ToString();
        }
    }
}