namespace Rix.Api;

/// <summary>Keeps queued PRs in a valid base-branch dependency order at all times, rather than
/// requiring callers to sort a snapshot before use — <see cref="TryEnqueue"/> only accepts a PR
/// if the resulting queue stays acyclic and orderable, so <see cref="Snapshot"/> is always ready
/// to submit as-is.</summary>
internal sealed class PrQueue
{
    private readonly Lock _lock = new();
    private readonly List<QueuedPr> _items = [];

    internal IResult TryEnqueue(QueuedPr pr)
    {
        lock (_lock)
        {
            if (_items.Any(item => item.Branch.Value == pr.Branch.Value))
                return Results.Conflict(new ErrorResponse($"Branch {pr.Branch.Value} is already queued."));

            // pr can create a transitive dependency between two already-queued items that were
            // previously unrelated (e.g. pr's base is one item and another item depends on pr),
            // which can require reordering those existing items relative to each other - not
            // just placing pr among them. So the whole order has to be re-derived from all the
            // constraints together, rather than only checking pr's own immediate bounds.
            var ordered = TryOrder([.. _items, pr]);

            if (ordered is null)
            {
                return Results.BadRequest
                (
                    new ErrorResponse($"Branch {pr.Branch.Value} would create a cyclic base-branch dependency among queued PRs.")
                );
            }

            _items.Clear();
            _items.AddRange(ordered);
            return Results.Ok(new QueuedResponse("queued"));
        }
    }

    internal IResult TryRemove(RixBranchName branch)
    {
        lock (_lock)
        {
            var index = _items.FindIndex(item => item.Branch.Value == branch.Value);
            if (index < 0)
                return Results.NotFound(new ErrorResponse($"No queued PR for branch {branch.Value}."));

            // Removing one item from an already-valid topological order leaves the remaining
            // items in a still-valid order, so no re-sort is needed here.
            _items.RemoveAt(index);
            return Results.Ok(new QueuedResponse("deleted"));
        }
    }

    internal IReadOnlyList<QueuedPr> Snapshot()
    {
        lock (_lock) { return _items.ToArray(); }
    }

    /// <summary>Orders items by branch/base-branch dependency so an item whose base branch is
    /// another item's branch (a stacked PR) comes after it. Repeated selection is O(n²), which
    /// is fine for the handful of PRs a single job run queues. Returns <c>null</c> if the
    /// base-branch relationships form a cycle.</summary>
    private static List<QueuedPr>? TryOrder(IReadOnlyList<QueuedPr> items)
    {
        var remaining = items.ToList();
        var ordered = new List<QueuedPr>(remaining.Count);
        while (remaining.Count > 0)
        {
            var index = remaining.FindIndex(item => !remaining.Any(other => other.Branch.Value == item.BaseBranch.Value));
            if (index < 0)
                return null;
            ordered.Add(remaining[index]);
            remaining.RemoveAt(index);
        }
        return ordered;
    }
}
