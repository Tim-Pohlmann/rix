namespace Rix.Job;

internal record JobConfig(
    RepoIdentifier Repo,
    string Prompt,
    ReadToken ReadToken,
    WriteToken WriteToken,
    MaxTokens MaxTokens,
    TimeoutMinutes TimeoutMinutes,
    string WorkDir
)
{
    internal const int DefaultMaxTokens = 50_000;
    internal const int DefaultTimeoutMinutes = 30;

    internal static JobConfig FromInputs(
        string repo,
        string prompt,
        string readToken,
        string writeToken,
        int? maxTokens,
        int? timeoutMinutes,
        string? workDir) =>
        new(
            Repo: new RepoIdentifier(repo),
            Prompt: prompt,
            ReadToken: new ReadToken(readToken),
            WriteToken: new WriteToken(writeToken),
            MaxTokens: new MaxTokens(maxTokens ?? DefaultMaxTokens),
            TimeoutMinutes: new TimeoutMinutes(timeoutMinutes ?? DefaultTimeoutMinutes),
            WorkDir: workDir ?? Path.GetTempPath()
        );

    internal IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Repo.Value))
            errors.Add("--repo is required");
        else if (!Repo.Value.Contains('/'))
            errors.Add("--repo must be in the format owner/repo");

        if (string.IsNullOrWhiteSpace(Prompt))
            errors.Add("--prompt is required");

        if (string.IsNullOrWhiteSpace(ReadToken.Value))
            errors.Add("--read-token is required");

        if (string.IsNullOrWhiteSpace(WriteToken.Value))
            errors.Add("--write-token is required");

        if (MaxTokens.Value <= 0)
            errors.Add("--max-tokens must be a positive integer");

        if (TimeoutMinutes.Value <= 0)
            errors.Add("--timeout must be a positive integer");

        if (!string.IsNullOrEmpty(WorkDir) && !Directory.Exists(WorkDir))
            errors.Add($"--work-dir does not exist: {WorkDir}");

        return errors;
    }
}
