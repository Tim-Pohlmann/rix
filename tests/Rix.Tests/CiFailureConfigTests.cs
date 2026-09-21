using Rix.Agents;
using Rix.CiFailure;
using Rix.Job;

namespace Rix.Tests;

[TestClass]
public class CiFailureConfigTests
{
    /// <summary>ToJobConfig copies nine values between two records whose fields are mostly the same
    /// shape - three consecutive <c>string?</c>s among them - by name, with nothing checking that
    /// each one lands in the slot it was read from. Two swapped arguments compile, pass every other
    /// test, and only show up as an agent run configured with someone else's values. So every value
    /// here is distinct and asserted individually, including the two ToJobConfig supplies itself.</summary>
    [TestMethod]
    public void ToJobConfig_CarriesEveryValueIntoItsOwnSlot()
    {
        var workDir = Directory.CreateTempSubdirectory("rix-tojob-work-").FullName;
        var outputDir = Directory.CreateTempSubdirectory("rix-tojob-out-").FullName;
        var config = new CiFailureConfig
        (
            RunId: new RunId(7),
            Repo: new RepoIdentifier("owner/name"),
            ReadToken: new GitReadToken("the-read-token"),
            TimeoutMinutes: new TimeoutMinutes(21),
            WorkDir: new DirectoryPath(workDir),
            OutputDir: new DirectoryPath(outputDir),
            Agent: AgentKind.Pi,
            MaxTokens: new MaxTokens(4321),
            MaxRixCommits: new MaxRixCommits(3),
            Model: "vendor/the-model",
            Credential: new AgentCredential("THE_API_KEY", "the-api-key")
        );

        var job = config.ToJobConfig("the prompt", new BranchName("rix/the-branch"));

        Assert.AreEqual("owner/name", job.Repo.ToString());
        Assert.AreEqual("the-read-token", job.ReadToken.Value);
        Assert.AreEqual(21, job.TimeoutMinutes.Value);
        Assert.AreEqual(workDir, job.WorkDir.Value);
        Assert.AreEqual(outputDir, job.OutputDir.Value);
        Assert.AreEqual(AgentKind.Pi, job.Agent.Kind);
        Assert.AreEqual(4321, job.Agent.MaxTokens.Value);
        Assert.AreEqual("vendor/the-model", job.Agent.Model);
        Assert.AreEqual(new AgentCredential("THE_API_KEY", "the-api-key"), job.Agent.Credential);
        Assert.AreEqual("the prompt", job.Agent.Prompt);
        Assert.AreEqual("rix/the-branch", job.AllowedPushBranches.Single().Value);
    }
}
