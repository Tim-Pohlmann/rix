namespace Rix.Api;

internal abstract record UpdateTaskValidation
{
    private protected UpdateTaskValidation() { }
}
internal sealed record ValidUpdateTask(QueuedTaskUpdate Update) : UpdateTaskValidation;
internal sealed record InvalidUpdateTask(string Reason) : UpdateTaskValidation;

internal static class UpdateTaskRequestExtensions
{
    extension(UpdateTaskRequest req)
    {
        public UpdateTaskValidation Validate()
        {
            if (string.IsNullOrWhiteSpace(req.Branch)) return new InvalidUpdateTask("branch is required");
            var hasTitle = !string.IsNullOrWhiteSpace(req.Title);
            var hasBody = !string.IsNullOrWhiteSpace(req.Body);
            if (!hasTitle && !hasBody) return new InvalidUpdateTask("at least one of title or body is required");

            return RixBranchName.Parse(req.Branch) switch
            {
                ParseError<RixBranchName> e => new InvalidUpdateTask($"branch: {e.Error}"),
                ParseSuccess<RixBranchName> branch => new ValidUpdateTask
                (
                    new QueuedTaskUpdate
                    (
                        branch.Value,
                        hasTitle switch { true => new PrTitle(req.Title!), false => null },
                        hasBody switch { true => new PrBody(req.Body!), false => null }
                    )
                ),
                _ => throw new InvalidOperationException($"Unexpected parse result: {req.Branch}"),
            };
        }
    }
}
