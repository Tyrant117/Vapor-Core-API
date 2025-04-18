using System;
using System.Collections.Generic;
using UnityEngine;

namespace Vapor.Observables
{
    [Serializable]
    public class ObservableList<T>
    {
        public delegate void ObservableListChangedEventHandler(ObservableList<T> sender, ChangeType changeType, int indexChanged);
        
        public enum ChangeType
        {
            Add,
            Remove,
            Swap,
            Clear,
        }

        public List<T> Items;

        public T this[int index] => Items[index];
        public int Count => Items.Count;

        public event ObservableListChangedEventHandler ListChanged;

        public ObservableList()
        {
            Items = new List<T>();
        }

        public ObservableList(int capacity)
        {
            Items = new List<T>(capacity);
        }

        public ObservableList(IList<T> baseList)
        {
            Items = new List<T>(baseList.Count);
            Items.AddRange(baseList);
        }

        private void Invoke(ChangeType changeType, int indexChanged) => ListChanged?.Invoke(this, changeType, indexChanged);

        public bool Swap(int index1, int index2)
        {
            if (!Items.IsValidIndex(index1) || !Items.IsValidIndex(index2))
            {
                return false;
            }

            (Items[index1], Items[index2]) = (Items[index2], Items[index1]);
            Invoke(ChangeType.Swap, -1);
            return true;
        }

        public void Clear()
        {
            Items.Clear();
            Invoke(ChangeType.Clear, -1);
        }

        public void Add(T item)
        {
            Items.Add(item);
            Invoke(ChangeType.Add, Items.Count - 1);
        }

        public void Insert(int index, T item)
        {
            Items.Insert(index, item);
            Invoke(ChangeType.Add, index);
        }

        public bool Remove(T item)
        {
            var idx = Items.IndexOf(item);
            return RemoveAt(idx);
        }

        public bool RemoveAt(int index)
        {
            if (!Items.IsValidIndex(index))
            {
                return false;
            }

            Items.RemoveAt(index);
            Invoke(ChangeType.Remove, index);
            return true;
        }
    }
}
