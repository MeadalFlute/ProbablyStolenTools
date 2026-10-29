using System;
using System.Collections.Generic;

namespace ProbablyStolenTools
{
    internal sealed class CheckpointHistory<T> where T : class, IDisposable
    {
        private readonly List<T> _items = new();
        private readonly int _capacity;
        private readonly Predicate<T> _countsTowardCapacity;
        private T _selected;
        internal IReadOnlyList<T> Items => _items;
        internal int Revision { get; private set; }

        internal CheckpointHistory(int capacity, Predicate<T> countsTowardCapacity = null)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
            _countsTowardCapacity = countsTowardCapacity ?? (_ => true);
        }

        internal void Add(T item, bool commitBranch = true)
        {
            if (item == null || _items.Contains(item)) throw new ArgumentException("Invalid history entry.");
            if (commitBranch) CommitBranch();
            _items.Add(item);
            while (_items.FindAll(_countsTowardCapacity).Count > _capacity)
            {
                Remove(_items.Find(_countsTowardCapacity));
            }
            Revision++;
        }

        // Selecting a checkpoint is reversible. Only a new customer commits the branch.
        internal void Select(T item)
        {
            if (!_items.Contains(item)) throw new InvalidOperationException("Checkpoint is no longer retained.");
            _selected = item;
        }

        internal bool IsFuture(T item)
            => _selected != null && _items.IndexOf(item) > _items.IndexOf(_selected);

        internal int CommitBranch()
        {
            if (_selected == null) return 0;
            int before = _items.Count;
            TruncateAfter(_selected);
            _selected = null;
            return before - _items.Count;
        }

        internal void Remove(T item)
        {
            if (!_items.Remove(item)) return;
            if (ReferenceEquals(_selected, item)) _selected = null;
            item.Dispose();
            Revision++;
        }

        internal void TruncateAfter(T item)
        {
            int index = _items.IndexOf(item);
            if (index < 0) throw new InvalidOperationException("Checkpoint is no longer retained.");
            for (int i = _items.Count - 1; i > index; i--)
            {
                var abandoned = _items[i];
                _items.RemoveAt(i);
                if (ReferenceEquals(_selected, abandoned)) _selected = null;
                abandoned.Dispose();
            }
            Revision++;
        }

        internal void Clear()
        {
            foreach (var item in _items) item.Dispose();
            _items.Clear();
            _selected = null;
            Revision++;
        }
    }
}
