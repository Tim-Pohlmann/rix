namespace Rix.Job;

internal record JobConfig(
    string Repo,
    string Prompt,
    string ReadToken,
    string WriteToken,
    int MaxTokens,
    int TimeoutMinutes,
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
            Repo: repo,
            Prompt: prompt,
            ReadToken: readToken,
            WriteToken: writeToken,
            MaxTokens: maxTokens ?? DefaultMaxTokens,
            TimeoutMinutes: timeoutMinutes ?? DefaultTimeoutMinutes,
            WorkDir: workDir ?? Path.GetTempPath()
        );

    internal IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Repo))
            errors.Add("--repo is required");
        else if (!Repo.Contains('/'))
            errors.Add("--repo must be in the format owner/repo");

        if (string.IsNullOrWhiteSpace(Prompt))
            errors.Add("--prompt is required");

        if (string.IsNullOrWhiteSpace(ReadToken))
            errors.Add("--read-token is required");

        if (string.IsNullOrWhiteSpace(WriteToken))
            errors.Add("--write-token is required");

        if (MaxTokens <= 0)
            errors.Add("--max-tokens must be a positive integer");

        if (TimeoutMinutes <= 0)
            errors.Add("--timeout must be a positive integer");

        if (!string.IsNullOrEmpty(WorkDir) && !Directory.Exists(WorkDir))
            errors.Add($"--work-dir does not exist: {WorkDir}");

        return errors;
    }
}
