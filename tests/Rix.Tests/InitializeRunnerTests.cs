using Rix.Initialize;

namespace Rix.Tests;

[TestClass]
public class InitializeRunnerTests
{
    private readonly List<(string Path, string Content)> _writes = [];
    private readonly StubLog _log = new();

    private InitializeContext Context(Func<string, string, Task>? writeAllText = null) => new
    (
        new StubFileSystem(writeAllText: writeAllText ?? ((path, content) =>
        {
            _writes.Add((path, content));
            return Task.CompletedTask;
        })),
        _log
    );

    private static Task<IInitializeResult> Run(InitializeConfig config, InitializeContext context)
    => InitializeRunner.RunAsync(config, context, CancellationToken.None);

    private static InitializeSuccess AssertSuccess(IInitializeResult result) => result switch
    {
        InitializeSuccess s => s,
        InitializeFailure f => throw new AssertFailedException($"expected success, got failure: {f.Error}"),
        _ => throw new AssertFailedException($"unexpected result: {result.GetType().Name}"),
    };

    [TestMethod]
    public async Task RunAsync_WritesEveryTemplate_UnderTheTargetDir()
    {
        var dir = Directory.CreateTempSubdirectory("rix-init-").FullName;
        AssertSuccess(await Run(TestConfig.ValidInitialize(dir), Context()));

        CollectionAssert.AreEqual
        (
            WorkflowTemplates.For(new StandaloneSetup(new("v0"))).Select(t => Path.Combine(dir, t.RelativePath)).ToArray(),
            _writes.Select(w => w.Path).ToArray()
        );
    }

    [TestMethod]
    public async Task RunAsync_WritesTemplateContent_WithReusableWorkflowRefsAndCiPlaceholder()
    {
        AssertSuccess(await Run(TestConfig.ValidInitialize(setup: new StandaloneSetup(new("release/2.x"))), Context()));

        var rixYml = _writes.Single(w => w.Path.EndsWith("workflows/rix.yml", StringComparison.Ordinal)).Content;
        var ciYml = _writes.Single(w => w.Path.EndsWith("rix-on-ci-failure.yml", StringComparison.Ordinal)).Content;

        StringAssert.Contains(rixYml, "uses: Tim-Pohlmann/rix/.github/workflows/agent.yml@release/2.x");
        StringAssert.Contains(rixYml, "read-token: ${{ secrets.RIX_READ_TOKEN }}");
        StringAssert.Contains(ciYml, "uses: Tim-Pohlmann/rix/.github/workflows/on-ci-failure.yml@release/2.x");
        StringAssert.Contains(ciYml, "workflows: [\"CI\"] # change \"CI\" to the name:");
        // Every occurrence is substituted, not just the first one the runner happens to hit.
        Assert.IsFalse(_writes.Any(w => w.Content.Contains(WorkflowTemplates.RefPlaceholder, StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task RunAsync_WritesTheFactoryTemplates_ForAFactorySetup()
    {
        var dir = Directory.CreateTempSubdirectory("rix-init-").FullName;
        AssertSuccess(await Run(TestConfig.ValidInitialize(dir, new FactorySetup(new("v9"))), Context()));

        CollectionAssert.AreEqual
        (
            WorkflowTemplates.For(new FactorySetup(new("v9"))).Select(t => (Path.Combine(dir, t.RelativePath), t.Content)).ToArray(),
            _writes.ToArray()
        );
    }

    /// <summary>The steps only make sense once the files they talk about exist, so they come last,
    /// after every <c>wrote</c> line.</summary>
    [TestMethod]
    public async Task RunAsync_LogsTheSetupsNextSteps_AfterTheWrittenFiles()
    {
        var setup = new FactorySetup(new("v9"));
        AssertSuccess(await Run(TestConfig.ValidInitialize(setup: setup), Context()));

        Assert.AreEqual(NextSteps.For(setup), _log.Lines[^1]);
        Assert.IsTrue(_log.Lines[..^1].All(line => line.StartsWith("wrote ", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task RunAsync_OverwritesSilently_WhenFilesAlreadyExist()
    {
        var dir = Directory.CreateTempSubdirectory("rix-init-").FullName;
        var config = TestConfig.ValidInitialize(dir);

        AssertSuccess(await Run(config, Context()));
        _writes.Clear();
        AssertSuccess(await Run(config, Context()));

        Assert.AreEqual(WorkflowTemplates.For(new StandaloneSetup(new("v0"))).Count, _writes.Count);
    }

    [TestMethod]
    public async Task RunAsync_ReturnsFailure_WhenAWriteThrowsIoException()
    {
        var result = await Run(TestConfig.ValidInitialize(),
            Context((_, _) => throw new IOException("disk full")));

        var failure = result switch
        {
            InitializeFailure f => f,
            _ => throw new AssertFailedException($"expected failure, got {result.GetType().Name}"),
        };
        // Names whichever template the runner reached first rather than a literal, so the
        // assertion keeps testing "the message says which file failed" if the discovered
        // order of the standalone templates changes.
        StringAssert.Contains(failure.Error, WorkflowTemplates.Of(new StandaloneSetup(new("v0")))[0].RelativePath);
        StringAssert.Contains(failure.Error, "disk full");
    }
}
