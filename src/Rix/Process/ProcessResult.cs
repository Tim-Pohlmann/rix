namespace Rix.Process;

internal record ProcessResult(int ExitCode, bool TimedOut)
{
    internal bool Succeeded => ExitCode == 0 && !TimedOut;
}
