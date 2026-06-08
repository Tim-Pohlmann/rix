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

            RixBranchName branch;
            try { branch = new RixBranchName(req.Branch); }
            catch (ArgumentException ex) { return new InvalidPr($"{nameof(RixBranchName)}: {ex.Message}"); }

            BranchName baseBranch;
            try { baseBranch = new BranchName(req.BaseBranch); }
            catch (ArgumentException ex) { return new InvalidPr($"{nameof(BranchName)}: {ex.Message}"); }

            PrTitle title;
            try { title = new PrTitle(req.Title); }
            catch (ArgumentException ex) { return new InvalidPr($"{nameof(PrTitle)}: {ex.Message}"); }

            PrBody body;
            try { body = new PrBody(req.Body); }
            catch (ArgumentException ex) { return new InvalidPr($"{nameof(PrBody)}: {ex.Message}"); }

            return new ValidPr(new QueuedPr(branch, baseBranch, title, body));
        }
    }
}
