namespace Rix.Initialize;

/// <summary>What <c>rix initialize</c> can't do itself: it only writes files, so the secrets, the
/// environment that holds the write token, and the commit are left to the user, along with
/// whatever else the setup needs. <see cref="InitializeRunner"/> logs them once the files are
/// written.</summary>
internal static class NextSteps
{
    internal static string For(IInitializeSetup setup) => setup switch
    {
        StandaloneSetup => """

            Next steps:
              1. Add the read token as a repo secret (Settings -> Secrets and variables -> Actions):
                   RIX_READ_TOKEN   PAT, contents:read  (+ actions:read for the CI-failure workflow)
              2. Add the write token to an environment named 'rix' (Settings -> Environments), and
                 limit that environment's deployment branches to your default branch:
                   RIX_WRITE_TOKEN  PAT, contents:write + pull-requests:write
              3. Commit and push the two workflow files.
            """,
        // The tokens span every served repo, and the write token's reach is what bounds the
        // factory, so the scope is spelled out. The read token reads the factory too, for its
        // agent home. The served repos still need a trigger of their own, since a workflow_run
        // cannot cross repos: the onboarding skill writes it, which takes the Workflows permission.
        FactorySetup => """

            Next steps:
              1. Add the read token as a repo secret (Settings -> Secrets and variables -> Actions):
                   RIX_READ_TOKEN   PAT, contents:read + actions:read on this repo and every repo
                                    it serves
              2. Add the write token to an environment named 'rix' (Settings -> Environments), and
                 limit that environment's deployment branches to your default branch:
                   RIX_WRITE_TOKEN  PAT, contents:write + pull-requests:write + workflows on exactly
                                    the repos this factory serves; it bounds where rix can open PRs
              3. Commit and push the written files: the two workflows and .rix/agent-home.
              4. Onboard each repo this factory serves: run this repo's "rix (agent)" workflow with
                 repo set to its owner/name and the prompt "Onboard this repo to the factory". The
                 PR it opens adds the workflow that dispatches the repo's CI failures here, and
                 lists the secrets left to set. To do it by hand instead, run this in a checkout of
                 that repo:
                   rix initialize <this repo's owner/name>
            """,
        // The dispatch token is the only secret a served repo holds; the factory's own tokens have
        // to reach it too, which nothing on this side can check, so the steps say so.
        FactoryServedSetup served => $"""

            Next steps:
              1. Add the dispatch token as a repo secret (Settings -> Secrets and variables -> Actions):
                   RIX_FACTORY_DISPATCH_TOKEN   PAT, contents:write on {served.FactoryRepo} only
              2. Make sure the factory's RIX_READ_TOKEN and RIX_WRITE_TOKEN include this repo.
              3. Commit and push the workflow file.
            """,
        _ => throw new NotSupportedException($"Unexpected initialize setup type: {setup.GetType()}"),
    };
}
