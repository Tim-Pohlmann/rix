namespace Rix;

/// <summary>Orders items by branch/base-branch dependency so an item whose base branch is another
/// item's branch (a stacked PR) comes after it — queue insertion order doesn't guarantee this.
/// Repeated selection is O(n²), which is fine for the handful of PRs a single job run queues.
/// Returns <c>null</c> if the base-branch relationships form a cycle.</summary>
internal static class PrDependencyOrder
{
    internal static List<T>? TryOrder<T>(IReadOnlyList<T> items, Func<T, string> branch, Func<T, string> baseBranch)
    {
        var remaining = items.ToList();
        var ordered = new List<T>(remaining.Count);
        while (remaining.Count > 0)
        {
            var index = remaining.FindIndex(item => !remaining.Any(other => branch(other) == baseBranch(item)));
            if (index < 0)
                return null;
            ordered.Add(remaining[index]);
            remaining.RemoveAt(index);
        }
        return ordered;
    }
}
