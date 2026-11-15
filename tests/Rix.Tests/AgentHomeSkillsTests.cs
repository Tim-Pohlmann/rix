using Rix.Initialize;
using System.Text.RegularExpressions;

namespace Rix.Tests;

[TestClass]
public partial class AgentHomeSkillsTests
{
    private static readonly IReadOnlyList<(string RelativePath, string Content)> FactorySkills =
        AgentHomeSkills.For(new FactorySetup(new("v0")));

    [TestMethod]
    public void For_Factory_WritesTheOnboardingSkill_WhereEveryHarnessLooks()
    => CollectionAssert.AreEqual
    (
        new[]
        {
            ".rix/agent-home/.claude/skills/rix-onboard/SKILL.md",
            ".rix/agent-home/.agents/skills/rix-onboard/SKILL.md",
        },
        FactorySkills.Select(s => s.RelativePath).ToArray()
    );

    /// <summary>opencode reads both directories, so two copies that differ would give it two skills
    /// under one name that disagree.</summary>
    [TestMethod]
    public void For_Factory_WritesTheSameSkillToEveryDirectory()
    => Assert.AreEqual(1, FactorySkills.Select(s => s.Content).Distinct().Count());

    /// <summary>Only a factory run has a factory to onboard a repo to.</summary>
    [TestMethod]
    public void For_WritesNoSkills_ForAnyOtherSetup()
    {
        Assert.IsEmpty(AgentHomeSkills.For(new StandaloneSetup(new("v0"), CiWorkflowName.Default)));
        Assert.IsEmpty(AgentHomeSkills.For(new FactoryServedSetup(new("acme/rix-factory"), CiWorkflowName.Default)));
    }

    /// <summary>The Agent Skills rules the harnesses share: the name is the directory's, in
    /// lowercase kebab case, and a skill without a description is never offered to the model (pi
    /// does not load it at all).</summary>
    [TestMethod]
    public void EverySkill_NamesItsDirectory_AndDescribesItself()
    {
        foreach (var (relativePath, content) in FactorySkills)
        {
            var frontmatter = Frontmatter().Match(content);
            Assert.IsTrue(frontmatter.Success, $"{relativePath} has no frontmatter");
            var directory = Path.GetFileName(Path.GetDirectoryName(relativePath))!;

            Assert.AreEqual(directory, Field(frontmatter, "name"));
            StringAssert.Matches(directory, SkillName());
            var description = Field(frontmatter, "description");
            Assert.IsTrue(description is { Length: > 0 and <= 1024 }, $"{relativePath}'s description is missing or too long");
        }
    }

    /// <summary>The skill writes the notify workflow with the served-repo setup of the binary
    /// running the agent, naming the factory the way a bare <c>rix initialize</c> takes it, rather
    /// than with a copy of the template that could drift.</summary>
    [TestMethod]
    public void TheOnboardingSkill_RunsTheServedRepoSetupOfThisRun()
    => StringAssert.Contains(FactorySkills[0].Content, "\"$RIX_BIN\" initialize \"$GITHUB_REPOSITORY\" --ci-workflow ");

    private static string Field(Match frontmatter, string key)
    => Regex.Match(frontmatter.Groups[1].Value, $"^{key}: (.*)$", RegexOptions.Multiline).Groups[1].Value.Trim();

    /// <summary>Either line ending: a Windows checkout embeds the skill with CRLF.</summary>
    [GeneratedRegex(@"\A---\r?\n(.*?)\r?\n---\r?\n", RegexOptions.Singleline)]
    private static partial Regex Frontmatter();

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SkillName();
}
