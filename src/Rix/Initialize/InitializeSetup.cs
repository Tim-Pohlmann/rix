namespace Rix.Initialize;

/// <summary>Which kind of repo <c>rix initialize</c> sets up. It picks the templates written and
/// the steps printed afterwards. Pattern-matched; never cast.</summary>
internal interface IInitializeSetup;

/// <summary>A repo that runs rix on itself and holds its own tokens.</summary>
internal sealed record StandaloneSetup : IInitializeSetup;

/// <summary>A factory repo: it runs rix against the repos it serves, holds the tokens for all of
/// them, and answers the CI failures they dispatch to it.</summary>
internal sealed record FactorySetup : IInitializeSetup;
