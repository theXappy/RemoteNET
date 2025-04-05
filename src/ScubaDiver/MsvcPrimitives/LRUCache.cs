using System.Collections;
using System.Collections.Generic;

namespace ScubaDiver;

public class LRUCache<TKey, TValue>
{
    private readonly int capacity;
    private readonly Dictionary<TKey, LinkedListNode<CacheItem>> cache;
    private readonly LinkedList<CacheItem> lruList;

    public LRUCache(int capacity)
    {
        this.capacity = capacity;
        cache = new Dictionary<TKey, LinkedListNode<CacheItem>>(capacity);
        lruList = new LinkedList<CacheItem>();
    }

    public void AddOrUpdate(TKey key, TValue value)
    {
        if (cache.ContainsKey(key))
        {
            // If the key is already in the cache, update its value and move it to the front of the LRU list
            LinkedListNode<CacheItem> node = cache[key];
            node.Value.Value = value;
            lruList.Remove(node);
            lruList.AddFirst(node);
        }
        else
        {
            // If the key is not in the cache, create a new node and add it to the cache and the front of the LRU list
            LinkedListNode<CacheItem> node = new LinkedListNode<CacheItem>(new CacheItem(key, value));
            cache.Add(key, node);
            lruList.AddFirst(node);

            // If the cache exceeds the capacity, remove the least recently used item from the cache and the LRU list
            if (cache.Count > capacity)
            {
                LinkedListNode<CacheItem> lastNode = lruList.Last;
                cache.Remove(lastNode.Value.Key);
                lruList.RemoveLast();
            }
        }
    }

    public bool TryGetValue(TKey key, bool delete, out TValue value)
    {
        if (cache.TryGetValue(key, out LinkedListNode<CacheItem> node))
        {
            // If the key is found in the cache, move it to the front of the LRU list and return its value
            lruList.Remove(node);
            if (delete)
            {
                cache.Remove(key);
            }
            else
            {
                lruList.AddFirst(node);
            }

            value = node.Value.Value;
            return true;
        }

        // If the key is not found in the cache, return default value for TValue
        value = default;
        return false;
    }

    private class CacheItem
    {
        public TKey Key { get; }
        public TValue Value { get; set; }

        public CacheItem(TKey key, TValue value)
        {
            Key = key;
            Value = value;
        }
    }
}

class LimitedSizeDictionary<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>
{
    private Dictionary<TKey, TValue> _this;
    Queue<TKey> queue;
    int limit;

    public LimitedSizeDictionary(int limit)
    {
        _this = new Dictionary<TKey, TValue>(limit + 1);
        this.limit = limit;
        queue = new Queue<TKey>(limit);
    }

    public void Add(TKey key, TValue value)
    {
        _this.Add(key, value);
        if (queue.Count == limit)
            this.Remove(queue.Dequeue());
        queue.Enqueue(key);
    }

    public bool Remove(TKey key)
    {
        if (_this.Remove(key))
        {
            Queue<TKey> newQueue = new Queue<TKey>(limit);
            foreach (TKey item in queue)
                if (!_this.Comparer.Equals(item, key))
                    newQueue.Enqueue(item);
            queue = newQueue;
            return true;
        }

        return false;
    }

    public bool Remove(TKey key, out TValue value)
    {
        _this.TryGetValue(key, out value);
        return _this.Remove(key);
    }

    public void AddOrUpdate(TKey key, TValue value)
    {
        Remove(key);
        Add(key, value);
    }

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => _this.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}