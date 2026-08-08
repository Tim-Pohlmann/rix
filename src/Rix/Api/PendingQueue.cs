namespace Rix.Api;

/// <summary>A thread-safe FIFO queue for the local API's pending requests. The agent enqueues
/// (POST), lists (GET) and cancels (DELETE) requests while the job is running; <c>rix</c> drains a
/// snapshot once the agent has finished. A plain <see cref="System.Collections.Concurrent.ConcurrentQueue{T}"/>
/// cannot remove matching items, so this wraps a lock-guarded list instead — every operation is
/// atomic against the others, so a cancel racing an enqueue cannot resurrect a removed item.</summary>
internal sealed class PendingQueue<T>
{
    private readonly List<T> _items = [];
    private readonly object _gate = new();

    /// <summary>Appends <paramref name="item"/> to the end of the queue, preserving submission order.</summary>
    internal void Enqueue(T item)
    {
        lock (_gate)
            _items.Add(item);
    }

    /// <summary>Returns a point-in-time copy of the queue in submission order.</summary>
    internal IReadOnlyList<T> Snapshot()
    {
        lock (_gate)
            return _items.ToArray();
    }

    /// <summary>Removes every item <paramref name="match"/> selects (the natural key is the queued
    /// branch, so duplicates of one branch are all cancelled together). Returns how many were removed.</summary>
    internal int RemoveAll(Predicate<T> match)
    {
        lock (_gate)
            return _items.RemoveAll(match);
    }
}
