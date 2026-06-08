namespace Rix.Job;

internal record JobConfig(
    RepoIdentifier Repo,
    string Prompt,
    ReadToken ReadToken,
    MaxTokens MaxTokens,
    TimeoutMinutes TimeoutMinutes,
    string WorkDir,
    string OutputDir
)
{
    internal const int DefaultMaxTokens = 50_000;
    internal const int DefaultTimeoutMinutes = 30;

    internal static JobConfig FromInputs(
        string repo,
        string prompt,
        string readToken,
        int? maxTokens,
        int? timeoutMinutes,
        string? workDir,
        string? outputDir) =>
        new(
            Repo: new RepoIdentifier(repo),
            Prompt: prompt,
            ReadToken: new ReadToken(readToken),
            MaxTokens: new MaxTokens(maxTokens ?? DefaultMaxTokens),
            TimeoutMinutes: new TimeoutMinutes(timeoutMinutes ?? DefaultTimeoutMinutes),
            WorkDir: string.IsNullOrWhiteSpace(workDir) ? Path.GetTempPath() : workDir,
            OutputDir: outputDir ?? string.Empty
        );
}

internal static class JobConfigExtensions
{
    extension(JobConfig config)
    {
        /// <summary>
        /// Pure validation: checks that only depend on the config values themselves
        /// (no filesystem or other I/O). Filesystem existence is checked separately by
        /// <see cref="FilesystemValidationErrors"/> at the imperative shell.
        /// </summary>
        public IReadOnlyList<string> ValidationErrors
        {
            get
            {
                var errors = new List<string>();

                if (string.IsNullOrWhiteSpace(config.Prompt))
                    errors.Add("--prompt is required");

                if (string.IsNullOrWhiteSpace(config.ReadToken.Value))
                    errors.Add("--read-token is required");

                if (config.MaxTokens.Value <= 0)
                    errors.Add("--max-tokens must be a positive integer");

                if (config.TimeoutMinutes.Value <= 0)
                    errors.Add("--timeout must be a positive integer");

                if (string.IsNullOrWhiteSpace(config.WorkDir))
                    errors.Add("--work-dir must not be empty");

                if (string.IsNullOrWhiteSpace(config.OutputDir))
                    errors.Add("--output-dir is required");

                return errors;
            }
        }

        /// <summary>
        /// Filesystem-dependent validation. The existence probe is injected so the logic
        /// stays pure and testable; the shell passes <see cref="Directory.Exists"/>.
        /// Directories whose names are empty are left to <see cref="ValidationErrors"/>.
        /// </summary>
        public IReadOnlyList<string> FilesystemValidationErrors(Func<string, bool> directoryExists)
        {
            var errors = new List<string>();

            if (!string.IsNullOrWhiteSpace(config.WorkDir) && !directoryExists(config.WorkDir))
                errors.Add($"--work-dir does not exist: {config.WorkDir}");

            if (!string.IsNullOrWhiteSpace(config.OutputDir) && !directoryExists(config.OutputDir))
                errors.Add($"--output-dir does not exist: {config.OutputDir}");

            return errors;
        }
    }
}
