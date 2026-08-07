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
    public async Task GetTasks_ReturnsOnlyTheReposRixTasks()
    {
        var host = new StubRepositoryHost(listOpenPullRequests: () => Task.FromResult<IReadOnlyList<RemotePr>>(
        [
            new RemotePr(1, "My task", "open", "rix/my-task", "main", "https://github.com/owner/repo/pull/1"),
            new RemotePr(2, "Other work", "open", "feature/other", "main", "https://github.com/owner/repo/pull/2"),
        ]));
        await using var server = await LocalApiServer.StartAsync(host, Path.GetTempPath(), CancellationToken.None);
        using var client = new HttpClient();

        var response = await client.GetAsync(new Uri(server.BaseUrl, "/tasks"));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual(1, doc.RootElement.GetArrayLength());
        Assert.AreEqual("rix/my-task", doc.RootElement[0].GetProperty("branch").GetString());
        Assert.AreEqual("My task", doc.RootElement[0].GetProperty("title").GetString());
    }

    [TestMethod]
    public async Task GetTasks_ReturnsEmptyList_WhenNoTasksSubmitted()
    {
        await using var server = await LocalApiServer.StartAsync(new StubRepositoryHost(), Path.GetTempPath(), CancellationToken.None);
        using var client = new HttpClient();

        var response = await client.GetAsync(new Uri(server.BaseUrl, "/tasks"));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.AreEqual("[]", json);
    }

    [TestMethod]
    public async Task GetTasks_Returns400_WhenGitHubListingFails()
    {
        var host = new StubRepositoryHost(listOpenPullRequests: () =>
            throw new HttpRequestException("rate limited"));
        await using var server = await LocalApiServer.StartAsync(host, Path.GetTempPath(), CancellationToken.None);
        using var client = new HttpClient();

        var response = await client.GetAsync(new Uri(server.BaseUrl, "/tasks"));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOpts)!;
        StringAssert.Contains(result["error"], "rate limited");
    }

    [TestMethod]
    public async Task PostUpdateTask_Returns200WithQueuedStatus()
    {
        await using var server = await LocalApiServer.StartAsync(FakeHost(true), Path.GetTempPath(), CancellationToken.None);
        using var client = new HttpClient();

        var response = await client.PostAsJsonAsync(new Uri(server.BaseUrl, "/tasks/update"), new
        {
            branch = "rix/my-fix",
            title = "Better title",
        });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOpts)!;
        Assert.AreEqual("queued", result["status"]);
    }

    [TestMethod]
    public async Task PostUpdateTask_RecordsPendingRequest()
    {
        await using var server = await LocalApiServer.StartAsync(FakeHost(true), Path.GetTempPath(), CancellationToken.None);
        using var client = new HttpClient();

        await client.PostAsJsonAsync(new Uri(server.BaseUrl, "/tasks/update"), new
        {
            branch = "rix/feat",
            title = "New title",
            body = "New body",
        });

        Assert.AreEqual(1, server.QueuedUpdateRequests.Count);
        Assert.AreEqual(new RixBranchName("rix/feat"), server.QueuedUpdateRequests[0].Branch);
        Assert.AreEqual(new PrTitle("New title"), server.QueuedUpdateRequests[0].Title);
        Assert.AreEqual(new PrBody("New body"), server.QueuedUpdateRequests[0].Body);
    }

    [TestMethod]
    public async Task PostUpdateTask_AllowsBodyOnly_KeepingTitleNull()
    {
        await using var server = await LocalApiServer.StartAsync(FakeHost(true), Path.GetTempPath(), CancellationToken.None);
        using var client = new HttpClient();

        var response = await client.PostAsJsonAsync(new Uri(server.BaseUrl, "/tasks/update"), new
        {
            branch = "rix/feat",
            body = "Only the body changed",
        });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(1, server.QueuedUpdateRequests.Count);
        Assert.IsNull(server.QueuedUpdateRequests[0].Title);
        Assert.AreEqual(new PrBody("Only the body changed"), server.QueuedUpdateRequests[0].Body);
    }

    [TestMethod]
    public async Task PostUpdateTask_Returns409_WhenBranchDoesNotExistOnRemote()
    {
        await using var server = await LocalApiServer.StartAsync(FakeHost(false), Path.GetTempPath(), CancellationToken.None);
        using var client = new HttpClient();

        var response = await client.PostAsJsonAsync(new Uri(server.BaseUrl, "/tasks/update"), new
        {
            branch = "rix/ghost",
            title = "Title",
        });

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOpts)!;
        StringAssert.Contains(result["error"], "rix/ghost");
        StringAssert.Contains(result["error"], "/pr");
    }

    [DataTestMethod]
    [DataRow("", "title", "branch is required")]
    [DataRow("rix/x", "", "at least one of title or body is required")]
    [DataRow("main", "Title", "rix/*")]
    public async Task PostUpdateTask_Returns400_ForInvalidRequest(string branch, string title, string expectedError)
    {
        await using var server = await LocalApiServer.StartAsync(FakeHost(true), Path.GetTempPath(), CancellationToken.None);
        using var client = new HttpClient();

        var response = await client.PostAsJsonAsync(new Uri(server.BaseUrl, "/tasks/update"), new
        {
            branch,
            title,
        });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOpts)!;
        StringAssert.Contains(result["error"], expectedError);
    }

    [TestMethod]
    public async Task PostRevertTask_Returns200WithQueuedStatus()
    {
        await using var server = await LocalApiServer.StartAsync(FakeHost(true), Path.GetTempPath(), CancellationToken.None);
        using var client = new HttpClient();

        var response = await client.PostAsJsonAsync(new Uri(server.BaseUrl, "/tasks/revert"), new
        {
            branch = "rix/my-fix",
        });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOpts)!;
        Assert.AreEqual("queued", result["status"]);
    }

    [TestMethod]
    public async Task PostRevertTask_RecordsPendingRequest()
    {
        await using var server = await LocalApiServer.StartAsync(FakeHost(true), Path.GetTempPath(), CancellationToken.None);
        using var client = new HttpClient();

        await client.PostAsJsonAsync(new Uri(server.BaseUrl, "/tasks/revert"), new
        {
            branch = "rix/feat",
        });

        Assert.AreEqual(1, server.QueuedRevertRequests.Count);
        Assert.AreEqual(new RixBranchName("rix/feat"), server.QueuedRevertRequests[0].Branch);
    }

    [TestMethod]
    public async Task PostRevertTask_Returns409_WhenBranchDoesNotExistOnRemote()
    {
        await using var server = await LocalApiServer.StartAsync(FakeHost(false), Path.GetTempPath(), CancellationToken.None);
        using var client = new HttpClient();

        var response = await client.PostAsJsonAsync(new Uri(server.BaseUrl, "/tasks/revert"), new
        {
            branch = "rix/ghost",
        });

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOpts)!;
        StringAssert.Contains(result["error"], "rix/ghost");
        StringAssert.Contains(result["error"], "/pr");
    }

    [DataTestMethod]
    [DataRow("", "branch is required")]
    [DataRow("main", "rix/*")]
    public async Task PostRevertTask_Returns400_ForInvalidRequest(string branch, string expectedError)
    {
        await using var server = await LocalApiServer.StartAsync(FakeHost(true), Path.GetTempPath(), CancellationToken.None);
        using var client = new HttpClient();

        var response = await client.PostAsJsonAsync(new Uri(server.BaseUrl, "/tasks/revert"), new
        {
            branch,
        });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOpts)!;
        StringAssert.Contains(result["error"], expectedError);
    }

}
