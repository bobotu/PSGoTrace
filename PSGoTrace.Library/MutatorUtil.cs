namespace PSGoTrace.Library
{
    public readonly struct MutatorUtil
    {
        public long Time { get; }
        public double Util { get; }

        public MutatorUtil(long time, double util)
        {
            Time = time;
            Util = util;
        }
    }
}