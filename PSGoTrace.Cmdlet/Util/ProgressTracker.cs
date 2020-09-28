using System;
using System.Management.Automation;
using PSGoTrace.Library;

namespace PSGoTrace.Cmdlet.Util
{
    public class ProgressTracker : IProgressTracker
    {
        public IProgress StartTask(string name, string description, IProgress? parent)
        {
            throw new System.NotImplementedException();
        }

        public void DoneTask(IProgress record)
        {
            throw new System.NotImplementedException();
        }
    }

    public class Progress : IProgress
    {
        private readonly ProgressRecord _record;

        public Progress(int id, string name, string description, int? parent = null)
        {
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
            }
        }
    }
}