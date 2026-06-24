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

            // Parse each field inline; a failure throws (prefixed with the field name) and the one
            // catch below turns the first such throw into an InvalidPr naming the rejected field.
            try
            {
                var branch = RixBranchName.Parse(req.Branch).Match(v => v, e => throw new InvalidFieldException($"branch: {e}"));
                var baseBranch = BranchName.Parse(req.BaseBranch).Match(v => v, e => throw new InvalidFieldException($"baseBranch: {e}"));
                var title = PrTitle.Parse(req.Title).Match(v => v, e => throw new InvalidFieldException($"title: {e}"));
                var body = PrBody.Parse(req.Body).Match(v => v, e => throw new InvalidFieldException($"body: {e}"));
                return new ValidPr(new QueuedPr(branch, baseBranch, title, body));
            }
            catch (InvalidFieldException ex)
            {
                return new InvalidPr(ex.Message);
            }
        }
    }

    /// <summary>Signals that a request field failed to parse; carries the field-prefixed reason so
    /// <see cref="PrRequestExtensions"/> can surface it as an <see cref="InvalidPr"/>.</summary>
    private sealed class InvalidFieldException(string message) : Exception(message);
}
