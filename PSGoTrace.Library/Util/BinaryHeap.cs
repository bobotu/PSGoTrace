using System.Collections.Generic;
using System.Linq;

namespace PSGoTrace.Library.Util
{
    internal class BinaryHeap<T>
    {
        private readonly IComparer<T> _comparer;
        private readonly List<T> _list;

        public BinaryHeap(List<T> list, IComparer<T> comparer)
        {
            _list = list;
            _comparer = comparer;
            var n = list.Count;
            for (var i = n / 2 - 1; i >= 0; i--) Down(i, n);
        }

        public BinaryHeap() : this(new List<T>(), Comparer<T>.Default) { }

        public BinaryHeap(List<T> list) : this(list, Comparer<T>.Default) { }

        public BinaryHeap(IComparer<T> comparer) : this(new List<T>(), comparer) { }

        public T Head => _list[0];

        public int Count => _list.Count;

        public T[] ToArray() => _list.ToArray();

        public void Add(T x)
        {
            _list.Add(x);
            Up(_list.Count - 1);
        }

        public T Pop()
        {
            var n = _list.Count - 1;
            Swap(0, n);
            Down(0, n);
            return RemoveLast();
        }

        public T Remove(int i)
        {
            var n = _list.Count - 1;
            if (n == i) return RemoveLast();
            Swap(i, n);
            if (!Down(i, n)) Up(i);
            return RemoveLast();
        }

        public void Fix(int i)
        {
            if (Down(i, _list.Count)) return;
            Up(i);
        }

        public IEnumerable<(int, T)> Elements()
        {
            return _list.Select((t, i) => (i, t));
        }

        private T RemoveLast()
        {
            var t = _list[^1];
            _list.RemoveAt(_list.Count - 1);
            return t;
        }

        private void Up(int j)
        {
            while (true)
            {
                var i = (j - 1) / 2;
                if (i == j || _comparer.Compare(_list[j], _list[i]) < 0) return;

                Swap(i, j);
                j = i;
            }
        }

        private bool Down(int i0, int n)
        {
            var i = i0;
            while (true)
            {
                var j1 = 2 * i + 1;
                if (j1 >= n || j1 < 0) break;
                int j = j1, j2 = j1 + 1;
                if (j2 < n && _comparer.Compare(_list[j2], _list[j1]) < 0) j = j2;
                if (_comparer.Compare(_list[j], _list[i]) >= 0) break;
                Swap(i, j);
                i = j;
            }

            return i > i0;
        }

        private void Swap(int i, int j)
        {
            var tmp = _list[i];
            _list[i] = _list[j];
            _list[j] = tmp;
        }
    }
}