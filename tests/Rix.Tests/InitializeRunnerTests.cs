using Rix.Initialize;

namespace Rix.Tests;

[TestClass]
public class InitializeRunnerTests
{
    private readonly List<(string Path, string Content)> _writes = [];
    private readonly List<string> _logs = [];

    private InitializeContext Context(WriteFileAsync? writeFile = null) => new
    (
        WriteFile: writeFile ?? ((path, content, _) =>
        {
            _writes.Add((path, content));
            return Task.CompletedTask;
        }),
        LogLine: _logs.Add
    );

    private static Task<IInitializeResult> Run(InitializeConfig config, InitializeContext context)
    => InitializeRunner.RunAsync(config, context, CancellationToken.None);

    private static InitializeSuccess AssertSuccess(IInitializeResult result) => result switch
    {
        InitializeSuccess s => s,
        InitializeFailure f => throw new AssertFailedException($"expected success, got failure: {f.Message}"),
        _ => throw new AssertFailedException($"unexpected result: {result.GetType().Name}"),
    };

    [TestMethod]
    public async Task RunAsync_WritesEveryTemplate_UnderTheTargetDir()
    {
        var dir = Directory.CreateTempSubdirectory("rix-init-").FullName;
        AssertSuccess(await Run(TestConfig.ValidInitialize(dir), Context()));

        CollectionAssert.AreEqual
        (
            WorkflowTemplates.All.Select(t => Path.Combine(dir, t.RelativePath)).ToArray(),
            _writes.Select(w => w.Path).ToArray()
        );
    }

    [TestMethod]
    public async Task RunAsync_WritesTemplateContent_WithReusableWorkflowRefsAndCiPlaceholder()
    {
        AssertSuccess(await Run(TestConfig.ValidInitialize(), Context()));

        var rixYml = _writes.Single(w => w.Path.EndsWith("workflows/rix.yml", StringComparison.Ordinal)).Content;
        var ciYml = _writes.Single(w => w.Path.EndsWith("rix-on-ci-failure.yml", StringComparison.Ordinal)).Content;

        StringAssert.Contains(rixYml, "uses: Tim-Pohlmann/rix/.github/workflows/job.yml@main");
        StringAssert.Contains(rixYml, "read-token: ${{ secrets.RIX_READ_TOKEN }}");
        StringAssert.Contains(ciYml, "uses: Tim-Pohlmann/rix/.github/workflows/on-ci-failure.yml@main");
        StringAssert.Contains(ciYml, "workflows: [\"CI\"] # change \"CI\" to the name:");
    }

    [TestMethod]
    public async Task RunAsync_OverwritesSilently_WhenFilesAlreadyExist()
    {
        var dir = Directory.CreateTempSubdirectory("rix-init-").FullName;
        var config = TestConfig.ValidInitialize(dir);

        AssertSuccess(await Run(config, Context()));
        _writes.Clear();
        AssertSuccess(await Run(config, Context()));

        Assert.AreEqual(WorkflowTemplates.All.Count, _writes.Count);
    }

    [TestMethod]
    public async Task RunAsync_ReturnsFailure_WhenAWriteThrowsIoException()
    {
        WriteFileAsync throwing = (_, _, _) => throw new IOException("disk full");
        var result = await Run(TestConfig.ValidInitialize(), Context(throwing));

        var failure = result switch
        {
            InitializeFailure f => f,
            _ => throw new AssertFailedException($"expected failure, got {result.GetType().Name}"),
        };
        StringAssert.Contains(failure.Message, "rix.yml");
        StringAssert.Contains(failure.Message, "disk full");
    }
}
