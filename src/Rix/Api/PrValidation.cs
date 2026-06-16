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

            // Each field is parsed on the ParseResult union, so an invalid value becomes a
            // collectable error rather than a thrown exception. The first failing field wins.
            return (RixBranchName.Parse(req.Branch), BranchName.Parse(req.BaseBranch),
                    PrTitle.Parse(req.Title), PrBody.Parse(req.Body)) switch
            {
                (ParseSuccess<RixBranchName> branch, ParseSuccess<BranchName> baseBranch,
                 ParseSuccess<PrTitle> title, ParseSuccess<PrBody> body)
                    => new ValidPr(new QueuedPr(branch.Value, baseBranch.Value, title.Value, body.Value)),
                (ParseError<RixBranchName> e, _, _, _) => new InvalidPr(e.Error),
                (_, ParseError<BranchName> e, _, _) => new InvalidPr(e.Error),
                (_, _, ParseError<PrTitle> e, _) => new InvalidPr(e.Error),
                (_, _, _, ParseError<PrBody> e) => new InvalidPr(e.Error),
                var other => throw new NotSupportedException($"Unexpected parse result combination: {other.GetType()}"),
            };
        }
    }
}
