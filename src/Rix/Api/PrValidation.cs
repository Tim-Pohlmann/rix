namespace Rix.Api;

internal abstract record PrValidation
{
    private protected PrValidation() { }
}
internal sealed record ValidPr(QueuedPr Pr) : PrValidation;
internal sealed record InvalidPr(string Reason) : PrValidation;

internal static class PrRequestExtensions
{
    extension(PrRequest req)
    {
        public PrValidation Validate()
        {
            if (string.IsNullOrWhiteSpace(req.Branch)) return new InvalidPr("branch is required");
            if (string.IsNullOrWhiteSpace(req.BaseBranch)) return new InvalidPr("baseBranch is required");
            if (string.IsNullOrWhiteSpace(req.Title)) return new InvalidPr("title is required");
            if (string.IsNullOrWhiteSpace(req.Body)) return new InvalidPr("body is required");

            // Each field is parsed on the ParseResult union; failures are carried alongside the
            // value rather than thrown, so every field flows through one error-handling path.
            var (branch, branchError) = Take(RixBranchName.Parse(req.Branch));
            var (baseBranch, baseBranchError) = Take(BranchName.Parse(req.BaseBranch));
            var (title, titleError) = Take(PrTitle.Parse(req.Title));
            var (body, bodyError) = Take(PrBody.Parse(req.Body));

            // The first field that failed (if any) becomes the error; otherwise every Take
            // returned its parsed value.
            var error = branchError ?? baseBranchError ?? titleError ?? bodyError;
            return error is not null
                ? new InvalidPr(error)
                : new ValidPr(new QueuedPr(branch!, baseBranch!, title, body));
        }
    }

    /// <summary>Projects a parse result onto a (value, error) pair: success carries the value and a
    /// null error; failure carries the default value and the error message.</summary>
    private static (T? Value, string? Error) Take<T>(ParseResult<T> result) =>
        result.Match<(T?, string?)>(value => (value, null), error => (default, error));
}
