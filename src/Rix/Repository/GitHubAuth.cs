using System.Text;

namespace Rix.Repository;

/// <summary>Builds the environment overrides that authenticate an HTTPS <c>git</c> subprocess against
/// github.com without ever placing the token in argv (visible via <c>ps</c>) or persisting it into a
/// clone's <c>.git/config</c> remote URL. Git reads these <c>GIT_CONFIG_*</c> variables as ad-hoc
/// config, so the credential is supplied only via the git subprocess environment for each
/// invocation. Shared by every component that shells out to authenticated git (<see cref="GitHubReadHost"/>,
/// <see cref="GitHubFactoryContextLoader"/>).</summary>
internal static class GitHubAuth
{
    internal static IReadOnlyDictionary<string, string> ExtraHeaderEnv(GitReadToken token)
    {
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"x-access-token:{token.Value}"));
        return new Dictionary<string, string>
        {
            ["GIT_CONFIG_COUNT"] = "1",
            ["GIT_CONFIG_KEY_0"] = "http.https://github.com/.extraheader",
            ["GIT_CONFIG_VALUE_0"] = $"Authorization: Basic {basic}",
        };
    }
}
