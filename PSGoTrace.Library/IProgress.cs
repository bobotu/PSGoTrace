namespace PSGoTrace.Library
{
    public interface IProgress
    {
        public int Id { get; }
        public float PercentComplete { get; set; }
    }

    public interface IProgressTracker
    {
        public IProgress StartTask(string name, string description, IProgress? parent);
        public void DoneTask(IProgress record);
    }
}