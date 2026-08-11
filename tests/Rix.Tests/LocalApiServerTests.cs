using Rix.Api;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Rix.Tests;

[TestClass]
public class LocalApiServerTests
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private static StubRepositoryHost FakeHost(bool branchExists) => new(_ => Task.FromResult(branchExists));

    [TestMethod]
    public async Task GetHealth_Returns200()
    {
        await using var server = await LocalApiServer.StartAsync(FakeHost(false), Path.GetTempPath(), CancellationToken.None);
        using var client = new HttpClient();

        var response = await client.GetAsync(new Uri(server.BaseUrl, "/health"));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task PostPr_Returns200WithQueuedStatus()
    {
        await using var server = await LocalApiServer.StartAsync(FakeHost(false), Path.GetTempPath(), CancellationToken.None);
        using var client = new HttpClient();

        var response = await client.PostAsJsonAsync(new Uri(server.BaseUrl, "/pr"), new
        {
            branch = "rix/my-fix",
            title = "Fix null ref",
            body = "Fixes the issue",
            baseBranch = "main",
        });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOpts)!;
        Assert.AreEqual("queued", result["status"]);
    }

    [TestMethod]
    public async Task PostPr_RecordsPendingRequest()
    {
        await using var server = await LocalApiServer.StartAsync(FakeHost(false), Path.GetTempPath(), CancellationToken.None);
        using var client = new HttpClient();

        await client.PostAsJsonAsync(new Uri(server.BaseUrl, "/pr"), new
        {
            branch = "rix/feat",
            title = "Add feature",
            body = "body",
            baseBranch = "main",
        });

        Assert.AreEqual(1, server.GetQueuedPrRequests().Count);
        Assert.AreEqual(new RixBranchName("rix/feat"), server.GetQueuedPrRequests()[0].Branch);
        Assert.AreEqual(new BranchName("main"), server.GetQueuedPrRequests()[0].BaseBranch);
        Assert.AreEqual(new PrTitle("Add feature"), server.GetQueuedPrRequests()[0].Title);
        Assert.AreEqual(new PrBody("body"), server.GetQueuedPrRequests()[0].Body);
    }

    [TestMethod]
    public async Task PostPr_PreservesQueueOrderAcrossMultipleBranches()
    {
        // Submission (rix submit) opens PRs in this order, and a later PR can be based on an
        // earlier one's branch (a stacked PR) — so queue order must match request order, not
        // whatever order the underlying storage happens to enumerate in.
        await using var server = await LocalApiServer.StartAsync(FakeHost(false), Path.GetTempPath(), CancellationToken.None);
        using var client = new HttpClient();

        foreach (var branch in new[] { "rix/first", "rix/second", "rix/third" })
        {
            await client.PostAsJsonAsync(new Uri(server.BaseUrl, "/pr"), new
            {
                branch,
                title = "title",
                body = "body",
                baseBranch = "main",
            });
        }

        CollectionAssert.AreEqual
        (
            new[] { "rix/first", "rix/second", "rix/third" },
            server.GetQueuedPrRequests().Select(pr => pr.Branch.Value).ToArray()
        );
    }

    [TestMethod]
    public async Task PostPr_Returns400_ForNonRixBranch()
    {
        await using var server = await LocalApiServer.StartAsync(FakeHost(false), Path.GetTempPath(), CancellationToken.None);
        using var client = new HttpClient();

        var response = await client.PostAsJsonAsync(new Uri(server.BaseUrl, "/pr"), new
        {
            branch = "main",
            title = "Title",
            body = "body",
            baseBranch = "main",
        });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOpts)!;
        StringAssert.Contains(result["error"], "rix/*");
        StringAssert.StartsWith(result["error"], "branch:");
    }

    [DataTestMethod]
    [DataRow("", "main", "Title", "body", "branch is required")]
    [DataRow("rix/x", "", "Title", "body", "baseBranch is required")]
    [DataRow("rix/x", "main", "", "body", "title is required")]
    [DataRow("rix/x", "main", "Title", "", "body is required")]
    public async Task PostPr_Returns400_ForMissingRequiredField(
        string branch, string baseBranch, string title, string body, string expectedError)
    {
        await using var server = await LocalApiServer.StartAsync(FakeHost(false), Path.GetTempPath(), CancellationToken.None);
        using var client = new HttpClient();

        var response = await client.PostAsJsonAsync(new Uri(server.BaseUrl, "/pr"), new
        {
            branch, title, body, baseBranch,
        });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOpts)!;
        Assert.AreEqual(expectedError, result["error"]);
    }

    [TestMethod]
    public async Task PostPr_Returns409_WhenBranchAlreadyExists()
    {
        await using var server = await LocalApiServer.StartAsync(FakeHost(true), Path.GetTempPath(), CancellationToken.None);
        using var client = new HttpClient();

        var response = await client.PostAsJsonAsync(new Uri(server.BaseUrl, "/pr"), new
        {
            branch = "rix/existing",
            title = "Title",
            body = "body",
            baseBranch = "main",
        });

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
    }

    [TestMethod]
    public async Task PostPr_Returns409_WhenBranchAlreadyQueued()
    {
        await using var server = await LocalApiServer.StartAsync(FakeHost(false), Path.GetTempPath(), CancellationToken.None);
        using var client = new HttpClient();

        var body = new { branch = "rix/feat", title = "Title", body = "body", baseBranch = "main" };
        await client.PostAsJsonAsync(new Uri(server.BaseUrl, "/pr"), body);
        var response = await client.PostAsJsonAsync(new Uri(server.BaseUrl, "/pr"), body);

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOpts)!;
        StringAssert.Contains(result["error"], "rix/feat");
        StringAssert.Contains(result["error"], "already queued");
        Assert.AreEqual(1, server.GetQueuedPrRequests().Count);
    }

    [TestMethod]
    public async Task PostPr_Returns400_WhenBranchNotFoundLocally()
    {
        var host = new StubRepositoryHost(branchExistsLocally: _ => Task.FromResult(false));
        await using var server = await LocalApiServer.StartAsync(host, Path.GetTempPath(), CancellationToken.None);
        using var client = new HttpClient();

        var response = await client.PostAsJsonAsync(new Uri(server.BaseUrl, "/pr"), new
        {
            branch = "rix/ghost",
            title = "Title",
            body = "body",
            baseBranch = "main",
        });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOpts)!;
        StringAssert.Contains(result["error"], "rix/ghost");
        StringAssert.Contains(result["error"], "working directory");
    }

    [TestMethod]
    public async Task PostPush_Returns200WithQueuedStatus()
    {
        await using var server = await LocalApiServer.StartAsync(
            FakeHost(true), Path.GetTempPath(), CancellationToken.None,
            allowedPushBranches: [new RixBranchName("rix/my-fix")]);
        using var client = new HttpClient();

        var response = await client.PostAsJsonAsync(new Uri(server.BaseUrl, "/push"), new
        {
            branch = "rix/my-fix",
            baseBranch = "main",
        });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOpts)!;
        Assert.AreEqual("queued", result["status"]);
    }

    [TestMethod]
    public async Task PostPush_RecordsPendingRequest()
    {
        await using var server = await LocalApiServer.StartAsync(
            FakeHost(true), Path.GetTempPath(), CancellationToken.None,
            allowedPushBranches: [new RixBranchName("rix/feat")]);
        using var client = new HttpClient();

        await client.PostAsJsonAsync(new Uri(server.BaseUrl, "/push"), new
        {
            branch = "rix/feat",
            baseBranch = "main",
        });

        Assert.AreEqual(1, server.GetQueuedPushRequests().Count);
        Assert.AreEqual(new RixBranchName("rix/feat"), server.GetQueuedPushRequests()[0].Branch);
        Assert.AreEqual(new BranchName("main"), server.GetQueuedPushRequests()[0].BaseBranch);
    }

    [TestMethod]
    public async Task PostPush_Returns400_ForNonRixBranch()
    {
        await using var server = await LocalApiServer.StartAsync(FakeHost(true), Path.GetTempPath(), CancellationToken.None);
        using var client = new HttpClient();

        var response = await client.PostAsJsonAsync(new Uri(server.BaseUrl, "/push"), new
        {
            branch = "main",
            baseBranch = "main",
        });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOpts)!;
        StringAssert.Contains(result["error"], "rix/*");
        StringAssert.StartsWith(result["error"], "branch:");
    }

    [DataTestMethod]
    [DataRow("", "main", "branch is required")]
    [DataRow("rix/x", "", "baseBranch is required")]
    public async Task PostPush_Returns400_ForMissingRequiredField(string branch, string baseBranch, string expectedError)
    {
        await using var server = await LocalApiServer.StartAsync(FakeHost(true), Path.GetTempPath(), CancellationToken.None);
        using var client = new HttpClient();

        var response = await client.PostAsJsonAsync(new Uri(server.BaseUrl, "/push"), new
        {
            branch, baseBranch,
        });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOpts)!;
        Assert.AreEqual(expectedError, result["error"]);
    }

    [TestMethod]
    public async Task PostPush_Returns409_WhenBranchDoesNotExistOnRemote()
    {
        await using var server = await LocalApiServer.StartAsync(
            FakeHost(false), Path.GetTempPath(), CancellationToken.None,
            allowedPushBranches: [new RixBranchName("rix/ghost")]);
        using var client = new HttpClient();

        var response = await client.PostAsJsonAsync(new Uri(server.BaseUrl, "/push"), new
        {
            branch = "rix/ghost",
            baseBranch = "main",
        });

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOpts)!;
        StringAssert.Contains(result["error"], "rix/ghost");
        StringAssert.Contains(result["error"], "/pr");
    }

    [TestMethod]
    public async Task PostPush_Returns409_WhenBranchAlreadyQueued()
    {
        await using var server = await LocalApiServer.StartAsync(
            FakeHost(true), Path.GetTempPath(), CancellationToken.None,
            allowedPushBranches: [new RixBranchName("rix/feat")]);
        using var client = new HttpClient();

        var body = new { branch = "rix/feat", baseBranch = "main" };
        await client.PostAsJsonAsync(new Uri(server.BaseUrl, "/push"), body);
        var response = await client.PostAsJsonAsync(new Uri(server.BaseUrl, "/push"), body);

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOpts)!;
        StringAssert.Contains(result["error"], "rix/feat");
        StringAssert.Contains(result["error"], "already queued");
        Assert.AreEqual(1, server.GetQueuedPushRequests().Count);
    }

    [TestMethod]
    public async Task PostPush_Returns400_WhenBranchNotFoundLocally()
    {
        var host = new StubRepositoryHost(
            branchExists: _ => Task.FromResult(true),
            branchExistsLocally: _ => Task.FromResult(false));
        await using var server = await LocalApiServer.StartAsync(
            host, Path.GetTempPath(), CancellationToken.None,
            allowedPushBranches: [new RixBranchName("rix/ghost")]);
        using var client = new HttpClient();

        var response = await client.PostAsJsonAsync(new Uri(server.BaseUrl, "/push"), new
        {
            branch = "rix/ghost",
            baseBranch = "main",
        });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOpts)!;
        StringAssert.Contains(result["error"], "rix/ghost");
        StringAssert.Contains(result["error"], "working directory");
    }

    [TestMethod]
    public async Task PostPush_Returns403_ByDefault_WhenNoAllowListConfigured()
    {
        await using var server = await LocalApiServer.StartAsync(FakeHost(true), Path.GetTempPath(), CancellationToken.None);
        using var client = new HttpClient();

        var response = await client.PostAsJsonAsync(new Uri(server.BaseUrl, "/push"), new
        {
            branch = "rix/my-fix",
            baseBranch = "main",
        });

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOpts)!;
        StringAssert.Contains(result["error"], "rix/my-fix");
        StringAssert.Contains(result["error"], "not allowed");
    }

    [TestMethod]
    public async Task PostPush_Returns403_WhenBranchNotAllowed()
    {
        await using var server = await LocalApiServer.StartAsync(
            FakeHost(true), Path.GetTempPath(), CancellationToken.None,
            allowedPushBranches: [new RixBranchName("rix/other")]);
        using var client = new HttpClient();

        var response = await client.PostAsJsonAsync(new Uri(server.BaseUrl, "/push"), new
        {
            branch = "rix/my-fix",
            baseBranch = "main",
        });

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOpts)!;
        StringAssert.Contains(result["error"], "rix/my-fix");
        StringAssert.Contains(result["error"], "not allowed");
        StringAssert.Contains(result["error"], "rix/other");
    }

    [TestMethod]
    public async Task PostPush_DoesNotQueue_WhenBranchNotAllowed()
    {
        await using var server = await LocalApiServer.StartAsync(
            FakeHost(true), Path.GetTempPath(), CancellationToken.None,
            allowedPushBranches: [new RixBranchName("rix/other")]);
        using var client = new HttpClient();

        var response = await client.PostAsJsonAsync(new Uri(server.BaseUrl, "/push"), new
        {
            branch = "rix/my-fix",
            baseBranch = "main",
        });

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.AreEqual(0, server.GetQueuedPushRequests().Count);
    }

    [TestMethod]
    public async Task PostPush_Returns200_WhenBranchIsAllowed()
    {
        await using var server = await LocalApiServer.StartAsync(
            FakeHost(true), Path.GetTempPath(), CancellationToken.None,
            allowedPushBranches: [new RixBranchName("rix/my-fix")]);
        using var client = new HttpClient();

        var response = await client.PostAsJsonAsync(new Uri(server.BaseUrl, "/push"), new
        {
            branch = "rix/my-fix",
            baseBranch = "main",
        });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(1, server.GetQueuedPushRequests().Count);
    }

    [TestMethod]
    public async Task PostPush_RejectsAllowedBranch_ThatIsNotInTheAllowList_CaseSensitively()
    {
        await using var server = await LocalApiServer.StartAsync(
            FakeHost(true), Path.GetTempPath(), CancellationToken.None,
            allowedPushBranches: [new RixBranchName("rix/My-Fix")]);
        using var client = new HttpClient();

        var response = await client.PostAsJsonAsync(new Uri(server.BaseUrl, "/push"), new
        {
            branch = "rix/my-fix",
            baseBranch = "main",
        });

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task PostPr_IsUnaffected_ByAllowedPushBranches()
    {
        await using var server = await LocalApiServer.StartAsync(
            FakeHost(false), Path.GetTempPath(), CancellationToken.None,
            allowedPushBranches: [new RixBranchName("rix/other")]);
        using var client = new HttpClient();

        var response = await client.PostAsJsonAsync(new Uri(server.BaseUrl, "/pr"), new
        {
            branch = "rix/my-fix",
            title = "Title",
            body = "body",
            baseBranch = "main",
        });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(1, server.GetQueuedPrRequests().Count);
    }

}
