namespace Rix.Initialize;

/// <summary>What <c>rix initialize</c> can't do itself: it only writes files, so the secrets, the
/// environment that holds the write token, and the commit are left to the user, along with
/// whatever else the setup needs. <see cref="InitializeRunner"/> logs them once the files are
/// written.</summary>
internal static class NextSteps
{
    internal static string For(IInitializeSetup setup) => setup switch
    {
        // Only a repo answering its own failures names the CI workflow it watches.
        StandaloneSetup => """

            Next steps:
              1. Add the read token as a repo secret (Settings -> Secrets and variables -> Actions):
                   RIX_READ_TOKEN   PAT, contents:read  (+ actions:read for the CI-failure workflow)
              2. Add the write token to an environment named 'rix' (Settings -> Environments), and
                 limit that environment's deployment branches to your default branch:
                   RIX_WRITE_TOKEN  PAT, contents:write + pull-requests:write
              3. In rix-on-ci-failure.yml, set workflows: ["CI"] to your CI workflow's name.
              4. Commit and push the two workflow files.
            """,
        // The tokens span every served repo, and the write token's reach is what bounds the
        // factory, so the scope is spelled out. The served repos still need a trigger of their
        // own, since a workflow_run cannot cross repos.
        FactorySetup => """

            Next steps:
              1. Add the read token as a repo secret (Settings -> Secrets and variables -> Actions):
                   RIX_READ_TOKEN   PAT, contents:read + actions:read on every repo this factory serves
              2. Add the write token to an environment named 'rix' (Settings -> Environments), and
                 limit that environment's deployment branches to your default branch:
                   RIX_WRITE_TOKEN  PAT, contents:write + pull-requests:write on exactly the repos
                                    this factory serves; it bounds where rix can open PRs
              3. Commit and push the two workflow files.
              4. In each repo this factory serves, add the workflow that dispatches its CI failures
                 here (README: "Advanced: a central factory repo").
            """,
        _ => throw new NotSupportedException($"Unexpected initialize setup type: {setup.GetType()}"),
    };
}
