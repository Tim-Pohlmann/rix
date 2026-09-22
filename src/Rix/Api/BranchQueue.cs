namespace Rix.Api;

/// <summary>The requests queued on one endpoint, keyed by branch: at most one request per branch,
/// cancellable by that branch, and listed in the order they will be delivered.</summary>
/// <param name="noun">What a request is called in error messages ("PR", "push").</param>
/// <param name="branchOf">The branch a request is queued under.</param>
internal class BranchQueue<T>(string noun, Func<T, BranchName> branchOf)
{
    private readonly Lock _lock = new();
    private readonly List<T> _items = [];

    // A branch already queued keeps its slot: without this, a second POST for the same branch would
    // report 200 "queued" while silently replacing the first request, so the caller would have no
    // way to tell its first call never went through.
    internal IResult TryEnqueue(T item)
    {
        var branch = branchOf(item);
        lock (_lock)
        {
            if (_items.Exists(queued => branchOf(queued).Value == branch.Value))
                return Results.Conflict(new ErrorResponse($"Branch {branch.Value} is already queued."));

            if (Admit(_items, item) is { } rejection)
                return Results.BadRequest(new ErrorResponse(rejection));

            return Results.Ok(new QueuedResponse("queued"));
        }
    }

    // A branch name with nothing queued is a 404 so the agent learns its cancel was a no-op
    // rather than assuming it took.
    internal IResult TryRemove(BranchName branch)
    {
        lock (_lock)
        {
            var index = _items.FindIndex(item => branchOf(item).Value == branch.Value);
            if (index < 0)
                return Results.NotFound(new ErrorResponse($"No queued {noun} for branch {branch.Value}."));

            // Removing one item from a valid delivery order leaves the rest in a still-valid order,
            // so no re-arranging is needed here.
            _items.RemoveAt(index);
            return Results.Ok(new QueuedResponse("deleted"));
        }
    }

    internal IReadOnlyList<T> Snapshot()
    {
        lock (_lock) { return _items.ToArray(); }
    }

    /// <summary>Adds <paramref name="item"/> (whose branch is not yet queued) to
    /// <paramref name="items"/>, keeping them in delivery order, or leaves <paramref name="items"/>
    /// untouched and returns why the item is refused. Called under the queue's lock. By default
    /// requests are delivered in the order they were queued.</summary>
    protected virtual string? Admit(List<T> items, T item)
    {
        items.Add(item);
        return null;
    }
}
