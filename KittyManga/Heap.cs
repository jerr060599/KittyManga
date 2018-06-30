//Copyright (c) 2018 Chi Cheng Hsu
//MIT License

using System;
using System.Collections;
using System.Collections.Generic;

namespace KittyManga {
    /// <summary>
    /// A min heap implementation
    /// </summary>
    /// <typeparam name="T"></typeparam>
    class Heap<T> : ICollection<T> {
        Func<T, T, int> compare;
        List<T> elements = new List<T>();

        public int Count => elements.Count;

        public bool IsReadOnly => true;

        public Heap(Func<T, T, int> comparasonFunc) {
            compare = comparasonFunc;
        }

        /// <summary>
        /// Adds an element to this heap
        /// </summary>
        /// <param name="ele"></param>
        public void Add(T ele) {
            lock (elements) {
                elements.Add(ele);
                Sort(elements.Count - 1);
            }
        }

        /// <summary>
        /// Gets the largest element
        /// </summary>
        /// <returns></returns>
        public T Peek() {
            lock (elements) {
                return elements[0];
            }
        }

        /// <summary>
        /// Gets the smallest element and removes it
        /// </summary>
        /// <returns></returns>
        public T Poll() {
            lock (elements) {
                T e = elements[0];
                RemoveAt(0);
                return e;
            }
        }

        /// <summary>
        /// Peeks the smallest element and replaces it
        /// </summary>
        /// <param name="e"></param>
        /// <returns></returns>
        public T PollAdd(T e) {
            lock (elements) {
                T ele = elements[0];
                elements[0] = e;
                Sort(0);
                return ele;
            }
        }

        /// <summary>
        /// Will only swap a and b is b is greater.
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns>Whether a swap was performed</returns>
        private bool TrySwap(int a, int b) {
            if (a < elements.Count && b < elements.Count)
                if (compare(elements[b], elements[a]) < 0) {
                    T tmp = elements[a];
                    elements[a] = elements[b];
                    elements[b] = tmp;
                    return true;
                }
            return false;
        }

        /// <summary>
        /// Assumes the element at index is out of order and sorts it accordingly
        /// </summary>
        /// <param name="i">The index</param>
        private void Sort(int i) {
            if (i != 0 && compare(elements[i], elements[(i - 1) >> 1]) < 0)//Bigger then parent
                while (true) {
                    if (i == 0) break;
                    int next = (i - 1) >> 1;
                    if (TrySwap(next, i))
                        i = next;
                    else break;
                }
            else {//Smaller then children
                while (true) {
                    int next = (i << 1) + 1;
                    if (next + 1 < elements.Count) {
                        if (compare(elements[next], elements[next + 1]) < 0) {
                            if (!TrySwap(i, next)) break;
                            i = next;
                        }
                        else if (!TrySwap(i, next + 1)) break;
                        else i = next + 1;
                    }
                    else {
                        TrySwap(i, next);
                        break;
                    }
                }
            }
        }

        public void Clear() {
            lock (elements) {
                elements.Clear();
            }
        }

        public bool Contains(T item) {
            lock (elements) {
                foreach (var a in elements)
                    if (a.Equals(item)) return true;
            }
            return false;
        }

        public void CopyTo(T[] array, int arrayIndex) {
            for (int i = 0; i < elements.Count; i++)
                array[i + arrayIndex] = elements[i];
        }

        public bool Remove(T item) {
            lock (elements) {
                for (int i = 0; i < elements.Count; i++)
                    if (elements[i].Equals(item)) {
                        RemoveAt(i);
                        return true;
                    }
            }
            return false;
        }

        private void RemoveAt(int i) {
            if (elements.Count == 1)
                elements.Clear();
            else {
                elements[i] = elements[elements.Count - 1];
                elements.RemoveAt(elements.Count - 1);
                Sort(i);
            }
        }

        public T[] ToArray() {
            T[] arr = new T[elements.Count];
            for (int i = 0; i < arr.Length; i++)
                arr[i] = elements[i];
            Array.Sort(arr, Comparer<T>.Create((a, b) => compare(a, b)));
            Array.Reverse(arr);
            return arr;
        }

        public List<T> ToList() {
            var l = new List<T>(elements);
            l.Sort(Comparer<T>.Create((a, b) => compare(a, b)));
            l.Reverse();
            return l;
        }

        public IEnumerator<T> GetEnumerator() {
            return elements.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return elements.GetEnumerator();
        }
    }
}
