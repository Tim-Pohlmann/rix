namespace Rix.Job;

internal record JobConfig(
    RepoIdentifier Repo,
    string Prompt,
    ReadToken ReadToken,
    WriteToken WriteToken,
    MaxTokens MaxTokens,
    TimeoutMinutes TimeoutMinutes,
    string WorkDir,
    string? AgentUser
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
        string? workDir,
        string? agentUser) =>
        new(
            Repo: new RepoIdentifier(repo),
            Prompt: prompt,
            ReadToken: new ReadToken(readToken),
            WriteToken: new WriteToken(writeToken),
            MaxTokens: new MaxTokens(maxTokens ?? DefaultMaxTokens),
            TimeoutMinutes: new TimeoutMinutes(timeoutMinutes ?? DefaultTimeoutMinutes),
            WorkDir: string.IsNullOrWhiteSpace(workDir) ? Path.GetTempPath() : workDir,
            AgentUser: string.IsNullOrWhiteSpace(agentUser) ? null : agentUser
        );
}

internal static class JobConfigExtensions
{
    extension(JobConfig config)
    {
        public IReadOnlyList<string> ValidationErrors
        {
            get
            {
                var errors = new List<string>();

                if (string.IsNullOrWhiteSpace(config.Repo.Value))
                    errors.Add("--repo is required");
                else if (!IsValidRepoFormat(config.Repo.Value))
                    errors.Add("--repo must be in the format owner/repo");

                if (string.IsNullOrWhiteSpace(config.Prompt))
                    errors.Add("--prompt is required");

                if (string.IsNullOrWhiteSpace(config.ReadToken.Value))
                    errors.Add("--read-token is required");

                if (string.IsNullOrWhiteSpace(config.WriteToken.Value))
                    errors.Add("--write-token is required");

                if (config.MaxTokens.Value <= 0)
                    errors.Add("--max-tokens must be a positive integer");

                if (config.TimeoutMinutes.Value <= 0)
                    errors.Add("--timeout must be a positive integer");

                if (string.IsNullOrWhiteSpace(config.WorkDir))
                    errors.Add("--work-dir must not be empty");
                else if (!Directory.Exists(config.WorkDir))
                    errors.Add($"--work-dir does not exist: {config.WorkDir}");

                return errors;
            }
        }
    }

    private static bool IsValidRepoFormat(string value)
    {
        var parts = value.Split('/');
        return parts.Length == 2
            && !string.IsNullOrWhiteSpace(parts[0])
            && !string.IsNullOrWhiteSpace(parts[1]);
    }
}
