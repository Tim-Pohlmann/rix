namespace Rix.Api;

internal abstract record DeleteValidation
{
    private protected DeleteValidation() { }
}
internal sealed record ValidDelete(RixBranchName Branch) : DeleteValidation;
internal sealed record InvalidDelete(string Reason) : DeleteValidation;

internal static class DeleteRequestExtensions
{
    extension(DeleteRequest req)
    {
        public DeleteValidation Validate()
        {
            if (string.IsNullOrWhiteSpace(req.Branch)) return new InvalidDelete("branch is required");

            return RixBranchName.Parse(req.Branch) switch
            {
                ParseError<RixBranchName> e => new InvalidDelete($"branch: {e.Error}"),
                ParseSuccess<RixBranchName> branch => new ValidDelete(branch.Value),
                var other => throw new InvalidOperationException($"Unexpected parse result: {other}"),
            };
        }
    }
}
