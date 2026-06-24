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

            // Parse each field on the ParseResult union; the first that fails short-circuits to an
            // InvalidPr naming the rejected field, otherwise every field parsed cleanly.
            if (RixBranchName.Parse(req.Branch) is ParseError<RixBranchName> branchError)
                return new InvalidPr($"branch: {branchError.Error}");
            if (BranchName.Parse(req.BaseBranch) is ParseError<BranchName> baseBranchError)
                return new InvalidPr($"baseBranch: {baseBranchError.Error}");
            if (PrTitle.Parse(req.Title) is ParseError<PrTitle> titleError)
                return new InvalidPr($"title: {titleError.Error}");
            if (PrBody.Parse(req.Body) is ParseError<PrBody> bodyError)
                return new InvalidPr($"body: {bodyError.Error}");

            return new ValidPr(new QueuedPr(
                new RixBranchName(req.Branch),
                new BranchName(req.BaseBranch),
                new PrTitle(req.Title),
                new PrBody(req.Body)));
        }
    }
}
