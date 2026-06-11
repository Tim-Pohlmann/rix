namespace Rix;

/// <summary>Process exit codes returned by <c>rix</c> commands.</summary>
internal static class ExitCodes
{
    /// <summary>The job completed successfully.</summary>
    internal const int Success = 0;

    /// <summary>The job ran but failed (e.g. the agent or git reported an error).</summary>
    internal const int JobFailed = 1;

    /// <summary>Setup failed before the job could run (invalid config or agent install).</summary>
    internal const int SetupFailed = 2;
}
