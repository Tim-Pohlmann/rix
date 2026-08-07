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

        Assert.AreEqual(1, server.QueuedPrRequests.Count);
        Assert.AreEqual(new RixBranchName("rix/feat"), server.QueuedPrRequests[0].Branch);
        Assert.AreEqual(new BranchName("main"), server.QueuedPrRequests[0].BaseBranch);
        Assert.AreEqual(new PrTitle("Add feature"), server.QueuedPrRequests[0].Title);
        Assert.AreEqual(new PrBody("body"), server.QueuedPrRequests[0].Body);
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
        await using var server = await LocalApiServer.StartAsync(FakeHost(true), Path.GetTempPath(), CancellationToken.None);
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
        await using var server = await LocalApiServer.StartAsync(FakeHost(true), Path.GetTempPath(), CancellationToken.None);
        using var client = new HttpClient();

        await client.PostAsJsonAsync(new Uri(server.BaseUrl, "/push"), new
        {
            branch = "rix/feat",
            baseBranch = "main",
        });

        Assert.AreEqual(1, server.QueuedPushRequests.Count);
        Assert.AreEqual(new RixBranchName("rix/feat"), server.QueuedPushRequests[0].Branch);
        Assert.AreEqual(new BranchName("main"), server.QueuedPushRequests[0].BaseBranch);
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
        await using var server = await LocalApiServer.StartAsync(FakeHost(false), Path.GetTempPath(), CancellationToken.None);
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
    public async Task PostPush_Returns400_WhenBranchNotFoundLocally()
    {
        var host = new StubRepositoryHost(
            branchExists: _ => Task.FromResult(true),
            branchExistsLocally: _ => Task.FromResult(false));
        await using var server = await LocalApiServer.StartAsync(host, Path.GetTempPath(), CancellationToken.None);
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
        Assert.AreEqual(0, server.QueuedPushRequests.Count);
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
        Assert.AreEqual(1, server.QueuedPushRequests.Count);
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
        Assert.AreEqual(1, server.QueuedPrRequests.Count);
    }

}
