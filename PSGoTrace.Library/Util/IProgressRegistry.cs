using System;

namespace PSGoTrace.Library.Util
{
    public interface IProgressRegistry
    {
        public IHandle Start(string name, string description, IHandle? parent = null);
        public IProgressRegistry Fork(IHandle parent);

        public interface IHandle : IDisposable
        {
            public int Id { get; }
            public float PercentComplete { set; }
            public string CurrentOperation { set; }
        }
    }
}