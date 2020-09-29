using System;
using System.Collections.Generic;

namespace PSGoTrace.Library.Helper
{
    internal class FibonacciHeap<T>
    {
        /// <summary>
        ///     The upper bound of fibonacci heap's rank is log1.618(n).
        ///     So if we track 4294967296 items in this heap,
        ///     we can know the size of consolidate table is around 46.09545510610244.
        /// </summary>
        private const int ConsolidateTableSize = 47;

        private readonly IComparer<T> _comparer;
        private readonly Node?[] _table = new Node[ConsolidateTableSize];
        private Node? _min;

        public FibonacciHeap()
        {
            _comparer = Comparer<T>.Default;
        }

        public FibonacciHeap(IComparer<T> comparer)
        {
            _comparer = comparer;
        }

        public int Count { get; private set; }

        public void Add(T data)
        {
            var node = new Node(data);
            Count++;
            if (_min is null)
            {
                _min = node;
                return;
            }

            node.Right = _min;
            node.Left = _min.Left;
            _min.Left = node;
            node.Left.Right = node;
            if (_comparer.Compare(data, _min.Data) < 0)
                _min = node;
        }

        public T Pop()
        {
            if (_min is null) throw new IndexOutOfRangeException();
            Node z = _min;

            if (z.Child != null)
            {
                // merge the children into root list
                var minLeft = _min.Left;
                var zChildLeft = z.Child.Left;
                _min.Left = zChildLeft;
                zChildLeft.Right = _min;
                z.Child.Left = minLeft;
                minLeft.Right = z.Child;
            }

            // remove z from root list of heap
            z.Left.Right = z.Right;
            z.Right.Left = z.Left;
            if (z == z.Right)
            {
                _min = null;
            }
            else
            {
                _min = z.Right;
                Consolidate();
            }

            Count--;
            return z.Data;
        }


        private void Consolidate()
        {
            if (_min is null) return;
            Array.Clear(_table, 0, ConsolidateTableSize);

            Node start = _min, w = _min;
            do
            {
                var x = w;
                var nextW = w.Right;
                var d = x.Degree;
                while (_table[d] != null)
                {
                    var y = _table[d];
                    if (_comparer.Compare(x.Data, y!.Data) > 0)
                    {
                        var temp = y;
                        y = x;
                        x = temp;
                    }

                    if (y == start)
                        // Because removeMin() arbitrarily assigned the min
                        // reference, we have to ensure we do not miss the
                        // end of the root node list.
                        start = start.Right;

                    if (y == nextW)
                        // If we wrapped around we need to check for this case.
                        nextW = nextW.Right;

                    y.Link(x);
                    _table[d] = null;
                    d++;
                }

                // Save this node for later when we might encounter another
                // of the same degree.
                _table[d] = x;
                w = nextW;
            } while (w != start);

            _min = start;
            foreach (var node in _table)
                if (node != null && _comparer.Compare(node.Data, _min.Data) < 0)
                    _min = node;
        }

        private class Node
        {
            public Node(T data)
            {
                Data = data;
                Left = this;
                Right = this;
            }

            public T Data { get; }
            public int Degree { get; set; }
            public Node Left { get; set; }
            public Node Right { get; set; }
            public Node? Child { get; set; }

            public void Cut(Node x, Node min)
            {
                x.Left.Right = x.Right;
                x.Right.Left = x.Left;
                Degree--;

                Child = Degree == 0 ? null : x.Right;
                x.Right = min;
                x.Left = min.Left;
                min.Left = x;
                x.Left.Right = x;
            }

            public void Link(Node parent)
            {
                Left.Right = Right;
                Right.Left = Left;

                if (parent.Child is null)
                {
                    parent.Child = this;
                    Right = this;
                    Left = this;
                }
                else
                {
                    Left = parent.Child;
                    Right = parent.Child.Right;
                    parent.Child.Right = this;
                    Right.Left = this;
                }

                parent.Degree++;
            }
        }
    }
}