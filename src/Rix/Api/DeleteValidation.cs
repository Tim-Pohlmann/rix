namespace Rix.Api;

internal abstract record DeleteValidation
{
    private protected DeleteValidation() { }
}
internal sealed record ValidDelete(BranchName Branch) : DeleteValidation;
internal sealed record InvalidDelete(string Reason) : DeleteValidation;

// Shared by both /pr DELETE and /push DELETE (see LocalApiServer.HandleDelete), so the branch
// name can't be restricted to rix/* here - only /pr's own POST enforces that when it creates
// the branch in the first place; deleting a queued push must accept whatever name was queued.
internal static class DeleteRequestExtensions
{
    extension(DeleteRequest req)
    {
        public DeleteValidation Validate()
        {
            if (string.IsNullOrWhiteSpace(req.Branch)) return new InvalidDelete("branch is required");

            // BranchName.Parse never fails (it doesn't validate format), so there's nothing to
            // match on here - just construct it directly.
            return new ValidDelete(new BranchName(req.Branch));
        }
    }
}
