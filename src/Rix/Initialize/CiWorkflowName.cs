namespace Rix.Initialize;

/// <summary>The <c>name:</c> of the workflow whose failures rix answers, which the written
/// <c>rix-on-ci-failure.yml</c> or <c>rix-notify-factory.yml</c> lists under
/// <c>workflow_run.workflows</c>. A workflow's name is
/// free text, spaces included, so it is written inside double quotes; validated because it is
/// interpolated there, and a quote, a backslash, or a control character would end the string
/// early or turn into an escape, giving a workflow that watches a different name or one GitHub
/// rejects at parse time - either way found out in the target repo's Actions tab, not here.</summary>
internal sealed record CiWorkflowName
{
    /// <summary>What <c>rix initialize</c> writes unless told otherwise: the name most CI workflows
    /// go by.</summary>
    internal static CiWorkflowName Default { get; } = new("CI");

    internal string Value { get; }

    internal CiWorkflowName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(c => c is '"' or '\\' || char.IsControl(c)))
            throw new InvalidInputException($"'{value}' is not a workflow name rix can write; expected the workflow's name: without quotes, backslashes, or control characters.");
        Value = value;
    }

    public override string ToString() => Value;
}
