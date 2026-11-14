namespace Rix;

/// <summary>Process exit codes returned by <c>rix</c> commands.</summary>
internal static class ExitCodes
{
    /// <summary>The command completed successfully.</summary>
    internal const int Success = 0;

    /// <summary>The command ran but failed (e.g. the agent or git reported an error).</summary>
    internal const int Failed = 1;

    /// <summary>Setup failed before the command could do its work (invalid config or agent install).</summary>
    internal const int SetupFailed = 2;

    /// <summary><c>result.json</c> could not be written. The run's own outcome is whatever it was,
    /// and is still on stdout, but the file <c>rix submit</c> and the job summary read it out of is
    /// missing - so the run cannot be treated as finished whatever that outcome was, and this code
    /// takes precedence over the three above. Distinct from them because the failure is the
    /// recording rather than the work: nothing was wrong with the config, the agent or the repo,
    /// and the reason is on stderr.</summary>
    internal const int ResultNotWritten = 3;
}
