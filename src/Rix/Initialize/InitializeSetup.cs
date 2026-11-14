namespace Rix.Initialize;

/// <summary>Which kind of repo <c>rix initialize</c> sets up, carrying what that kind needs: the
/// templates it writes, what fills them in, and the steps it leaves to the user. Adding a setup is
/// adding a record and its template folder; nothing switches over them.</summary>
internal interface IInitializeSetup
{
    /// <summary>The folder under <c>Cli/Templates/</c> holding this setup's workflow templates (see
    /// <see cref="WorkflowTemplates"/>).</summary>
    string TemplateFolder { get; }

    /// <summary>Each placeholder this setup's templates carry (see <see cref="WorkflowTemplates"/>),
    /// with what replaces it.</summary>
    IReadOnlyDictionary<string, string> Placeholders { get; }

    /// <summary>What <c>rix initialize</c> can't do itself: it only writes files, so the secrets,
    /// the environment that holds the write token, and the commit are left to the user, along with
    /// whatever else this setup needs. Printed after the files are written.</summary>
    string NextSteps { get; }
}

/// <summary>A repo that runs rix on itself and holds its own tokens. Its workflows call rix's at
/// <see cref="Ref"/>. Its next steps also name the
/// watched CI workflow, which only a repo answering its own failures has.</summary>
internal sealed record StandaloneSetup(WorkflowRef Ref) : IInitializeSetup
{
    public string TemplateFolder => "Standalone";

    public IReadOnlyDictionary<string, string> Placeholders
    => new Dictionary<string, string> { [WorkflowTemplates.RefPlaceholder] = Ref.Value };

    public string NextSteps => """

        Next steps:
          1. Add the read token as a repo secret (Settings -> Secrets and variables -> Actions):
               RIX_READ_TOKEN   PAT, contents:read  (+ actions:read for the CI-failure workflow)
          2. Add the write token to an environment named 'rix' (Settings -> Environments), and
             limit that environment's deployment branches to the ones these workflows run on:
               RIX_WRITE_TOKEN  PAT, contents:write + pull-requests:write
          3. In rix-on-ci-failure.yml, set workflows: ["CI"] to your CI workflow's name.
          4. Commit and push the two workflow files.
        """;
}

/// <summary>A factory repo: it runs rix, at <see cref="Ref"/>, against the repos it serves, holds the tokens for all of
/// them, and answers the CI failures they dispatch to it. The tokens span every served repo, and
/// the write token's reach is what bounds the factory, so its next steps spell out the scope. The
/// served repos still need a trigger of their own, since a workflow_run cannot cross repos.</summary>
internal sealed record FactorySetup(WorkflowRef Ref) : IInitializeSetup
{
    public string TemplateFolder => "Factory";

    public IReadOnlyDictionary<string, string> Placeholders
    => new Dictionary<string, string> { [WorkflowTemplates.RefPlaceholder] = Ref.Value };

    public string NextSteps => """

        Next steps:
          1. Add the read token as a repo secret (Settings -> Secrets and variables -> Actions):
               RIX_READ_TOKEN   PAT, contents:read + actions:read on every repo this factory serves
          2. Add the write token to an environment named 'rix' (Settings -> Environments), and
             limit that environment's deployment branches to this repo's default branch:
               RIX_WRITE_TOKEN  PAT, contents:write + pull-requests:write on exactly the repos
                                this factory serves; it bounds where rix can open PRs
          3. Commit and push the two workflow files.
          4. In each repo this factory serves, add the workflow that dispatches its CI failures
             here (README: "Advanced: a central factory repo").
        """;
}
