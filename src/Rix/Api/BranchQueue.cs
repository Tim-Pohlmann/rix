using System.Collections.Concurrent;

namespace Rix.Api;

/// <summary>A branch-keyed queue of pending requests: at most one item per branch (via
/// <see cref="TryAdd"/>), snapshotted in the order items were added. A plain
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> gives the atomic per-branch dedup but its
/// enumeration order is unspecified, which silently broke stacked-PR submission (the base branch
/// must be pushed and opened before the branch stacked on it) — this pairs the dictionary with a
/// per-item insertion sequence so <see cref="Snapshot"/> can restore that order.</summary>
internal sealed class BranchQueue<T>
{
    private readonly ConcurrentDictionary<string, (long Seq, T Item)> _items = new();
    private long _nextSeq;

    public bool TryAdd(string branch, T item)
        => _items.TryAdd(branch, (Interlocked.Increment(ref _nextSeq), item));

    public bool TryRemove(string branch) => _items.TryRemove(branch, out _);

    public T[] Snapshot()
        => _items.Values.OrderBy(entry => entry.Seq).Select(entry => entry.Item).ToArray();
}
