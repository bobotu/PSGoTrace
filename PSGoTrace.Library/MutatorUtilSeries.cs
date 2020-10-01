using System;
using System.Collections;
using System.Collections.Generic;

namespace PSGoTrace.Library
{
    public class MutatorUtilSeries : IReadOnlyList<MutatorUtil>
    {
        private readonly List<MutatorUtil> _utils = new List<MutatorUtil>();

        public IEnumerator<MutatorUtil> GetEnumerator() => _utils.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable) _utils).GetEnumerator();

        public int Count => _utils.Count;
        public MutatorUtil this[int index] => _utils[index];

        internal void Add(MutatorUtil mu)
        {
            if (Count > 0)
            {
                if (Math.Abs(mu.Util - _utils[^1].Util) < 1e-5) return;
                if (mu.Time == _utils[^1].Time)
                {
                    if (mu.Util < _utils[^1].Util) _utils[^1] = mu;
                    return;
                }
            }

            _utils.Add(mu);
        }
    }
}