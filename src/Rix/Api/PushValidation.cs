namespace Rix.Api;

internal abstract record PushValidation
{
    private protected PushValidation() { }
}
internal sealed record ValidPush(QueuedPush Push) : PushValidation;
internal sealed record InvalidPush(string Reason) : PushValidation;

internal static class PushRequestExtensions
{
    extension(PushRequest req)
    {
        public PushValidation Validate()
        {
            if (string.IsNullOrWhiteSpace(req.Branch)) return new InvalidPush("branch is required");
            if (string.IsNullOrWhiteSpace(req.BaseBranch)) return new InvalidPush("baseBranch is required");

            // Unlike /pr, the branch here already exists on the remote (see HandlePushAsync), so it
            // isn't a name the agent is inventing - any branch name is acceptable, not just rix/*.
            // BranchName.Parse never fails (it doesn't validate format), so there's nothing to
            // match on here - just construct both directly.
            return new ValidPush(new QueuedPush(new BranchName(req.Branch), new BranchName(req.BaseBranch)));
        }
    }
}
