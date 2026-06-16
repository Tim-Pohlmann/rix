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

            // Each field is parsed on the ParseResult union; the first failure (if any) is recorded
            // in `error` rather than thrown, so every field flows through one error-handling path.
            string? error = null;
            var branch = Take(RixBranchName.Parse(req.Branch), ref error);
            var baseBranch = Take(BranchName.Parse(req.BaseBranch), ref error);
            var title = Take(PrTitle.Parse(req.Title), ref error);
            var body = Take(PrBody.Parse(req.Body), ref error);

            // When no field failed, every Take returned its parsed value.
            return error is null
                ? new ValidPr(new QueuedPr(branch!, baseBranch!, title, body))
                : new InvalidPr(error);
        }
    }

    /// <summary>Unwraps a successful parse to its value; on failure records the first
    /// <paramref name="error"/> and returns the default so the caller can keep collecting.</summary>
    private static T? Take<T>(ParseResult<T> result, ref string? error)
    {
        if (result is ParseSuccess<T> ok) return ok.Value;
        if (result is ParseError<T> bad) error ??= bad.Error;
        return default;
    }
}
