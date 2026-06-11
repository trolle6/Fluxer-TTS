using System.Collections.Concurrent;

namespace WaveTechFluxerTTS.Utils;

public sealed class LruCache<TValue> where TValue : class
{
    private readonly int _maxSize;
    private readonly TimeSpan _ttl;
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new();
    private readonly LinkedList<string> _order = new();
    private readonly object _orderLock = new();

    public LruCache(int maxSize, TimeSpan ttl)
    {
        _maxSize = Math.Max(1, maxSize);
        _ttl = ttl;
    }

    public bool TryGet(string key, out TValue? value)
    {
        value = null;
        if (!_entries.TryGetValue(key, out var entry))
            return false;
        if (DateTime.UtcNow - entry.CreatedUtc > _ttl)
        {
            Remove(key);
            return false;
        }

        Touch(key);
        value = entry.Value;
        return true;
    }

    public void Set(string key, TValue value)
    {
        _entries[key] = new CacheEntry(value, DateTime.UtcNow);
        Touch(key);
        EvictIfNeeded();
    }

    private void Touch(string key)
    {
        lock (_orderLock)
        {
            _order.Remove(key);
            _order.AddFirst(key);
        }
    }

    private void Remove(string key)
    {
        _entries.TryRemove(key, out _);
        lock (_orderLock)
            _order.Remove(key);
    }

    private void EvictIfNeeded()
    {
        lock (_orderLock)
        {
            while (_order.Count > _maxSize)
            {
                var oldest = _order.Last;
                if (oldest is null)
                    break;
                _order.RemoveLast();
                _entries.TryRemove(oldest.Value, out _);
            }
        }
    }

    private sealed record CacheEntry(TValue Value, DateTime CreatedUtc);
}
