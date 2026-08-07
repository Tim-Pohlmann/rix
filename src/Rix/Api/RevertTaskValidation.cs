namespace Rix.Api;

internal abstract record RevertTaskValidation
{
    private protected RevertTaskValidation() { }
}
internal sealed record ValidRevertTask(QueuedTaskRevert Revert) : RevertTaskValidation;
internal sealed record InvalidRevertTask(string Reason) : RevertTaskValidation;

internal static class RevertTaskRequestExtensions
{
    extension(RevertTaskRequest req)
    {
        public RevertTaskValidation Validate()
        {
            if (string.IsNullOrWhiteSpace(req.Branch)) return new InvalidRevertTask("branch is required");

            return RixBranchName.Parse(req.Branch) switch
            {
                ParseError<RixBranchName> e => new InvalidRevertTask($"branch: {e.Error}"),
                ParseSuccess<RixBranchName> branch => new ValidRevertTask(new QueuedTaskRevert(branch.Value)),
                _ => throw new InvalidOperationException($"Unexpected parse result: {req.Branch}"),
            };
        }
    }
}
