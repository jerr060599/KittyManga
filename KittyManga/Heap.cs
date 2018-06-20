//Copyright (c) 2018 Chi Cheng Hsu
//MIT License

using System;
using System.Collections;
using System.Collections.Generic;

namespace KittyManga {
    /// <summary>
    /// A quick implementaion of a heap using list.
    /// O(n) insertion/deletion, O(k) query. 
    /// </summary>
    /// <typeparam name="T"></typeparam>
    class Heap<T> : IEnumerable<T> {
        List<T> Elements = new List<T>();
        IComparer<T> comparer;

        public int Count {
            get { return Elements.Count; }
        }
        public T Min {
            get { return Elements[Elements.Count - 1]; }
        }

        public T this[int i] {
            get { return Elements[i]; }
        }

        public Heap(Func<T, T, int> c) {
            comparer = Comparer<T>.Create((a, b) => c(a, b));
        }

        public Heap(IComparer<T> c) {
            comparer = c;
        }

        public void Add(T item) {
            if (Elements.Count == 0) {
                Elements.Add(item);
                return;
            }
            int low = 0, high = Elements.Count, mid = -1;
            while (low < high) {
                mid = (high + low) / 2;
                int r = comparer.Compare(item, Elements[mid]);
                if (r == 0)
                    break;
                else if (r > 0)
                    high = mid;
                else
                    low = mid + 1;
            }
            if (comparer.Compare(item, Elements[mid]) > 0)
                Elements.Insert(mid, item);
            else
                Elements.Insert(mid + 1, item);
        }

        public void Clear() {
            Elements.Clear();
        }

        public T Last() {
            return Elements[Elements.Count - 1];
        }

        public void RemoveMin() {
            Elements.RemoveAt(Elements.Count - 1);
        }

        public void RemoveAt(int i) {
            Elements.RemoveAt(i);
        }

        public void Remove(T ele) {
            Elements.Remove(ele);
        }

        public List<T> ToList() {
            return new List<T>(Elements);
        }

        public T[] ToArray() {
            return Elements.ToArray();
        }

        public IEnumerator<T> GetEnumerator() {
            return Elements.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return Elements.GetEnumerator();
        }
    }
}
