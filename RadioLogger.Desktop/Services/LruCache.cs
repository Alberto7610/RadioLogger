using System;
using System.Collections.Generic;

namespace RadioLogger.Services
{
    /// <summary>
    /// Caché LRU (Least Recently Used) genérico con operaciones O(1).
    /// No es thread-safe; su uso debe estar serializado por el caller (UI thread o lock externo).
    /// </summary>
    public class LruCache<TKey, TValue> where TKey : notnull
    {
        private readonly int _capacity;
        private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> _index;
        private readonly LinkedList<KeyValuePair<TKey, TValue>> _order;

        public LruCache(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
            _index = new Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>>(capacity);
            _order = new LinkedList<KeyValuePair<TKey, TValue>>();
        }

        public int Count => _index.Count;
        public int Capacity => _capacity;

        public bool TryGet(TKey key, out TValue value)
        {
            if (_index.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
            value = default!;
            return false;
        }

        public void Set(TKey key, TValue value)
        {
            if (_index.TryGetValue(key, out var existing))
            {
                _order.Remove(existing);
                var refreshed = new LinkedListNode<KeyValuePair<TKey, TValue>>(
                    new KeyValuePair<TKey, TValue>(key, value));
                _order.AddFirst(refreshed);
                _index[key] = refreshed;
                return;
            }

            var node = new LinkedListNode<KeyValuePair<TKey, TValue>>(
                new KeyValuePair<TKey, TValue>(key, value));
            _order.AddFirst(node);
            _index[key] = node;

            if (_index.Count > _capacity)
            {
                var oldest = _order.Last!;
                _order.RemoveLast();
                _index.Remove(oldest.Value.Key);
            }
        }

        public void Clear()
        {
            _index.Clear();
            _order.Clear();
        }
    }
}
