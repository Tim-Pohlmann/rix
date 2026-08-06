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

            // Parse every field once, then match: the first failure becomes an InvalidPush naming
            // the field, and the all-success arm hands the parsed strong types straight to QueuedPush.
            return (RixBranchName.Parse(req.Branch),
                    BranchName.Parse(req.BaseBranch)) switch
            {
                (ParseError<RixBranchName> e, _) => new InvalidPush($"branch: {e.Error}"),
                (_, ParseError<BranchName> e) => new InvalidPush($"baseBranch: {e.Error}"),
                (ParseSuccess<RixBranchName> branch, ParseSuccess<BranchName> baseBranch)
                    => new ValidPush(new QueuedPush(branch.Value, baseBranch.Value)),
                var other => throw new InvalidOperationException($"Unexpected parse results: {other}"),
            };
        }
    }
}
