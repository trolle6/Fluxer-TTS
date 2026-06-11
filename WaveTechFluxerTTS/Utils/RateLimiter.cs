using System.Collections.Concurrent;

namespace WaveTechFluxerTTS.Utils;

public sealed class RateLimiter
{
    private readonly int _limit;
    private readonly TimeSpan _window;
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _buckets = new();

    public RateLimiter(int limit, TimeSpan window)
    {
        _limit = Math.Max(1, limit);
        _window = window;
    }

    public bool TryAcquire(string key)
    {
        var now = DateTime.UtcNow;
        var queue = _buckets.GetOrAdd(key, _ => new Queue<DateTime>());
        lock (queue)
        {
            while (queue.Count > 0 && now - queue.Peek() > _window)
                queue.Dequeue();
            if (queue.Count >= _limit)
                return false;
            queue.Enqueue(now);
            return true;
        }
    }
}
