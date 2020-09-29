#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Threading;
using PSGoTrace.Library;

namespace PSGoTrace.Util
{
    public class ProgressRegistry : IProgressRegistry
    {
        private readonly Cmdlet _cmdlet;
        private readonly IdGenerator _generator;
        private readonly IProgressRegistry.IHandle? _parent;

        public ProgressRegistry(Cmdlet cmdlet)
        {
            _cmdlet = cmdlet;
            _generator = new IdGenerator();
        }

        private ProgressRegistry(ProgressRegistry source, IProgressRegistry.IHandle parent)
        {
            _cmdlet = source._cmdlet;
            _generator = source._generator;
            _parent = parent;
        }

        public IProgressRegistry.IHandle Start(string name, string description,
            IProgressRegistry.IHandle? parent = null)
        {
            var progress = new Progress(_generator.GetUniqId(), name, description, WriteProgress,
                parent?.Id ?? _parent?.Id);
            return progress;
        }

        public IProgressRegistry Fork(IProgressRegistry.IHandle parent)
        {
            return new ProgressRegistry(this, parent);
        }

        private void WriteProgress(ProgressRecord record)
        {
            _cmdlet.WriteProgress(record);
        }


        private class IdGenerator
        {
            private int _counter = 0;

            public int GetUniqId() => Interlocked.Increment(ref _counter);
        }

        private class Progress : IProgressRegistry.IHandle
        {
            private readonly ProgressRecord _record;
            private readonly Action<ProgressRecord> _notify;

            public Progress(int id, string name, string description, Action<ProgressRecord> notify, int? parent = null)
            {
                _notify = notify;
                _record = new ProgressRecord(id, name, description);
                if (parent.HasValue) _record.ParentActivityId = parent.Value;
            }

            public int Id => _record.ActivityId;

            public float PercentComplete
            {
                get => _record.PercentComplete;
                set
                {
                    _record.PercentComplete = (int) value;
                    _notify.Invoke(_record);
                }
            }

            public string CurrentOperation
            {
                get => _record.CurrentOperation;
                set
                {
                    _record.CurrentOperation = value;
                    _notify.Invoke(_record);
                }
            }

            public void Dispose()
            {
                _record.RecordType = ProgressRecordType.Completed;
                _record.PercentComplete = 100;
                _notify.Invoke(_record);
            }
        }
    }
}