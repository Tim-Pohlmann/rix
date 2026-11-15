namespace Rix.Initialize;

/// <summary>Which kind of repo <c>rix initialize</c> sets up, with the values that kind takes.
/// Pattern-matched by what acts on it (<see cref="WorkflowTemplates"/>, <see cref="NextSteps"/>);
/// never cast.</summary>
internal interface IInitializeSetup;

/// <summary>A repo that runs rix on itself and holds its own tokens.</summary>
/// <param name="Ref">The revision of rix its workflows call.</param>
internal sealed record StandaloneSetup(WorkflowRef Ref) : IInitializeSetup;

/// <summary>A factory repo: it runs rix against the repos it serves, holds the tokens for all of
/// them, and answers the CI failures they dispatch to it.</summary>
/// <param name="Ref">The revision of rix its workflows call.</param>
internal sealed record FactorySetup(WorkflowRef Ref) : IInitializeSetup;
