using Rix.Api;
using Rix.Repository;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Rix.Tests;

[TestClass]
public class LocalApiServerTests
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private static readonly HttpClient Client = new();

    private static readonly string[] JustRixA = ["rix/a"];
    private static readonly string[] BaseThenStacked = ["rix/base", "rix/stacked"];
    private static readonly string[] ReorderedCbA = ["rix/c", "rix/b", "rix/a"];

    private static StubJobRepoHost FakeHost(bool branchExists) => new(_ => Task.FromResult(branchExists));

    private static Task<LocalApiServer> StartAsync(IJobRepoHost host, IReadOnlyList<BranchName>? allowedPushBranches = null)
    => LocalApiServer.StartAsync(host, Path.GetTempPath(), CancellationToken.None, allowedPushBranches: allowedPushBranches);

    private static Task<HttpResponseMessage> GetAsync(LocalApiServer server, string path)
    => Client.GetAsync(new Uri(server.BaseUrl, path));

    private static Task<HttpResponseMessage> PostPrAsync
    (
        LocalApiServer server,
        string branch,
        string baseBranch = "main",
        string title = "Title",
        string body = "body"
    )
    => Client.PostAsJsonAsync(new Uri(server.BaseUrl, "/pr"), new { branch, title, body, baseBranch });

    private static Task<HttpResponseMessage> PostPushAsync(LocalApiServer server, string branch, string baseBranch = "main")
    => Client.PostAsJsonAsync(new Uri(server.BaseUrl, "/push"), new { branch, baseBranch });

    private static Task<HttpResponseMessage> DeleteAsync(LocalApiServer server, string path, string branch)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(server.BaseUrl, path))
        {
            Content = JsonContent.Create(new { branch }, options: JsonOpts),
        };
        return Client.SendAsync(request);
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response)
    => JsonSerializer.Deserialize<T>(await response.Content.ReadAsStringAsync(), JsonOpts)!;

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    => (await ReadJsonAsync<ErrorResponse>(response)).Error;

    private static async Task<string> ReadStatusAsync(HttpResponseMessage response)
    => (await ReadJsonAsync<QueuedResponse>(response)).Status;

    [TestMethod]
    public async Task GetHealth_Returns200()
    {
        await using var server = await StartAsync(FakeHost(false));

        var response = await GetAsync(server, "/health");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task GetOpenApi_ServesSpecDescribingThePrAndPushEndpoints()
    {
        await using var server = await StartAsync(FakeHost(false));

        var response = await GetAsync(server, "/openapi.json");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        StringAssert.StartsWith(root.GetProperty("openapi").GetString(), "3.");
        var paths = root.GetProperty("paths");
        Assert.IsTrue(paths.TryGetProperty("/pr", out var pr), "spec must document /pr");
        Assert.IsTrue(pr.TryGetProperty("post", out var postPr), "spec must document POST /pr");
        Assert.IsTrue(paths.TryGetProperty("/push", out var push), "spec must document /push");
        Assert.AreEqual("delivery", postPr.GetProperty("tags")[0].GetString());
        Assert.AreEqual("delivery", push.GetProperty("delete").GetProperty("tags")[0].GetString());

        // The POST /pr request body schema is derived from PrRequest, so its fields must show up.
        var rawText = root.GetRawText();
        foreach (var field in new[] { "branch", "title", "body", "baseBranch" })
            StringAssert.Contains(rawText, $"\"{field}\"");
    }

    [TestMethod]
    public async Task GetOpenApi_FoldsThePushAllowListIntoTheSpec()
    {
        await using var server = await StartAsync(FakeHost(true), [new RixBranchName("rix/continue-x")]);

        var response = await GetAsync(server, "/openapi.json");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var specText = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(specText, "rix/continue-x");
    }

    [TestMethod]
    public async Task GetOpenApi_SaysPushIsDisabled_WhenNoAllowListConfigured()
    {
        await using var server = await StartAsync(FakeHost(false));

        var specText = await (await GetAsync(server, "/openapi.json")).Content.ReadAsStringAsync();

        StringAssert.Contains(specText, "rejects every request");
    }

    [TestMethod]
    public async Task PostPr_Returns200WithQueuedStatus()
    {
        await using var server = await StartAsync(FakeHost(false));

        var response = await PostPrAsync(server, "rix/my-fix", title: "Fix null ref", body: "Fixes the issue");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("queued", await ReadStatusAsync(response));
    }

    [TestMethod]
    public async Task PostPr_RecordsPendingRequest()
    {
        await using var server = await StartAsync(FakeHost(false));

        await PostPrAsync(server, "rix/feat", title: "Add feature");

        Assert.AreEqual(1, server.GetQueuedPrRequests().Count);
        Assert.AreEqual(new RixBranchName("rix/feat"), server.GetQueuedPrRequests()[0].Branch);
        Assert.AreEqual(new BranchName("main"), server.GetQueuedPrRequests()[0].BaseBranch);
        Assert.AreEqual(new PrTitle("Add feature"), server.GetQueuedPrRequests()[0].Title);
        Assert.AreEqual(new PrBody("body"), server.GetQueuedPrRequests()[0].Body);
    }

    [TestMethod]
    public async Task PostPr_Returns400_ForNonRixBranch()
    {
        await using var server = await StartAsync(FakeHost(false));

        var response = await PostPrAsync(server, "main");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await ReadErrorAsync(response);
        StringAssert.Contains(error, "rix/*");
        StringAssert.StartsWith(error, "branch:");
    }

    [DataTestMethod]
    [DataRow("", "main", "Title", "body", "branch is required")]
    [DataRow("rix/x", "", "Title", "body", "baseBranch is required")]
    [DataRow("rix/x", "main", "", "body", "title is required")]
    [DataRow("rix/x", "main", "Title", "", "body is required")]
    public async Task PostPr_Returns400_ForMissingRequiredField(
        string branch, string baseBranch, string title, string body, string expectedError)
    {
        await using var server = await StartAsync(FakeHost(false));

        var response = await PostPrAsync(server, branch, baseBranch, title, body);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(expectedError, await ReadErrorAsync(response));
    }

    [TestMethod]
    public async Task PostPr_Returns409_WhenBranchAlreadyExists()
    {
        await using var server = await StartAsync(FakeHost(true));

        var response = await PostPrAsync(server, "rix/existing");

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
    }

    [TestMethod]
    public async Task PostPr_Returns409_WhenBranchAlreadyQueued()
    {
        await using var server = await StartAsync(FakeHost(false));

        await PostPrAsync(server, "rix/feat");
        var response = await PostPrAsync(server, "rix/feat");

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        var error = await ReadErrorAsync(response);
        StringAssert.Contains(error, "rix/feat");
        StringAssert.Contains(error, "already queued");
        Assert.AreEqual(1, server.GetQueuedPrRequests().Count);
    }

    [TestMethod]
    public async Task PostPr_Returns400_WhenQueuingWouldCreateCyclicBaseBranchDependency()
    {
        // Submission opens PRs base-first, so a cycle among queued PRs' base branches (here:
        // rix/a based on rix/b, and rix/b based on rix/a) can never be submitted — reject it at
        // queue time rather than letting the agent's session end before the problem surfaces.
        await using var server = await StartAsync(FakeHost(false));

        await PostPrAsync(server, "rix/a", baseBranch: "rix/b");
        var response = await PostPrAsync(server, "rix/b", baseBranch: "rix/a");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await ReadErrorAsync(response);
        StringAssert.Contains(error, "rix/b");
        StringAssert.Contains(error, "cyclic");
        CollectionAssert.AreEqual(
            JustRixA, server.GetQueuedPrRequests().Select(pr => pr.Branch.Value).ToArray());
    }

    [TestMethod]
    public async Task PostPr_Accepts_NonCyclicStackedPr_AndReturnsQueueInDependencyOrder()
    {
        // rix/stacked is queued before its own base branch rix/base — submission needs the queue
        // in base-first order regardless of the order PRs were queued in, so GetQueuedPrRequests()
        // must reflect the corrected order, not insertion order.
        await using var server = await StartAsync(FakeHost(false));

        await PostPrAsync(server, "rix/stacked", baseBranch: "rix/base");
        var response = await PostPrAsync(server, "rix/base");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        CollectionAssert.AreEqual(
            BaseThenStacked,
            server.GetQueuedPrRequests().Select(pr => pr.Branch.Value).ToArray());
    }

    [TestMethod]
    public async Task PostPr_Accepts_PrThatReordersTwoUnrelatedExistingPrs()
    {
        // rix/a (based on rix/b) and rix/c (based on rix/d) are unrelated when first queued, so
        // either order between them is valid. Queuing rix/b based on rix/c then chains them
        // transitively (c -> b -> a), which requires rix/c to move before rix/a even though
        // neither of them individually conflicts with rix/b's own bounds - a true topological
        // reorder, not just an insertion.
        await using var server = await StartAsync(FakeHost(false));

        await PostPrAsync(server, "rix/a", baseBranch: "rix/b");
        await PostPrAsync(server, "rix/c", baseBranch: "rix/d");
        var response = await PostPrAsync(server, "rix/b", baseBranch: "rix/c");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        CollectionAssert.AreEqual(
            ReorderedCbA,
            server.GetQueuedPrRequests().Select(pr => pr.Branch.Value).ToArray());
    }

    [TestMethod]
    public async Task PostPr_Returns400_WhenBranchNotFoundLocally()
    {
        await using var server = await StartAsync(new StubJobRepoHost(branchExistsLocally: _ => Task.FromResult(false)));

        var response = await PostPrAsync(server, "rix/ghost");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await ReadErrorAsync(response);
        StringAssert.Contains(error, "rix/ghost");
        StringAssert.Contains(error, "working directory");
    }

    [TestMethod]
    public async Task PostPr_Returns502_WhenRepositoryHostThrows()
    {
        // The remote-branch check calls the GitHub API; when that transport fails the request
        // can't be judged either way, so the middleware maps the one exception those checks throw
        // to a 502 rather than letting it leak out of the handler as an unhandled 500.
        var host = new StubJobRepoHost(
            branchExists: _ => throw new RepoHostException("check branch rix/my-fix on remote failed: 503"));
        await using var server = await StartAsync(host);

        var response = await PostPrAsync(server, "rix/my-fix");

        Assert.AreEqual(HttpStatusCode.BadGateway, response.StatusCode);
        var error = await ReadErrorAsync(response);
        StringAssert.Contains(error, "repository host error");
        StringAssert.Contains(error, "503");
        Assert.AreEqual(0, server.GetQueuedPrRequests().Count);
    }

    [TestMethod]
    public async Task PostPush_Returns502_WhenRepositoryHostThrows()
    {
        var host = new StubJobRepoHost(
            branchExists: _ => throw new RepoHostException("check branch rix/my-fix on remote failed: 503"));
        await using var server = await StartAsync(host, [new BranchName("rix/my-fix")]);

        var response = await PostPushAsync(server, "rix/my-fix");

        Assert.AreEqual(HttpStatusCode.BadGateway, response.StatusCode);
        StringAssert.Contains(await ReadErrorAsync(response), "repository host error");
        Assert.AreEqual(0, server.GetQueuedPushRequests().Count);
    }

    [TestMethod]
    public async Task PostPush_Returns200WithQueuedStatus()
    {
        await using var server = await StartAsync(FakeHost(true), [new BranchName("rix/my-fix")]);

        var response = await PostPushAsync(server, "rix/my-fix");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("queued", await ReadStatusAsync(response));
    }

    [TestMethod]
    public async Task PostPush_RecordsPendingRequest()
    {
        await using var server = await StartAsync(FakeHost(true), [new BranchName("rix/feat")]);

        await PostPushAsync(server, "rix/feat");

        Assert.AreEqual(1, server.GetQueuedPushRequests().Count);
        Assert.AreEqual(new BranchName("rix/feat"), server.GetQueuedPushRequests()[0].Branch);
        Assert.AreEqual(new BranchName("main"), server.GetQueuedPushRequests()[0].BaseBranch);
    }

    [TestMethod]
    public async Task PostPush_Accepts_NonRixBranch_WhenAllowed()
    {
        // Unlike /pr (which names a branch the agent invents), /push targets a branch that
        // already exists on the remote - including a human's own, non-rix/* branch - so only
        // the allow-list restricts it, never the rix/* naming pattern.
        await using var server = await StartAsync(FakeHost(true), [new BranchName("feature/human-work")]);

        var response = await PostPushAsync(server, "feature/human-work");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(1, server.GetQueuedPushRequests().Count);
        Assert.AreEqual(new BranchName("feature/human-work"), server.GetQueuedPushRequests()[0].Branch);
        Assert.AreEqual(new BranchName("main"), server.GetQueuedPushRequests()[0].BaseBranch);
    }

    [DataTestMethod]
    [DataRow("", "main", "branch is required")]
    [DataRow("rix/x", "", "baseBranch is required")]
    public async Task PostPush_Returns400_ForMissingRequiredField(string branch, string baseBranch, string expectedError)
    {
        await using var server = await StartAsync(FakeHost(true));

        var response = await PostPushAsync(server, branch, baseBranch);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(expectedError, await ReadErrorAsync(response));
    }

    [TestMethod]
    public async Task PostPush_Returns409_WhenBranchDoesNotExistOnRemote()
    {
        await using var server = await StartAsync(FakeHost(false), [new BranchName("rix/ghost")]);

        var response = await PostPushAsync(server, "rix/ghost");

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        var error = await ReadErrorAsync(response);
        StringAssert.Contains(error, "rix/ghost");
        StringAssert.Contains(error, "/pr");
    }

    [TestMethod]
    public async Task PostPush_Returns409_WhenBranchAlreadyQueued()
    {
        await using var server = await StartAsync(FakeHost(true), [new BranchName("rix/feat")]);

        await PostPushAsync(server, "rix/feat");
        var response = await PostPushAsync(server, "rix/feat");

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        var error = await ReadErrorAsync(response);
        StringAssert.Contains(error, "rix/feat");
        StringAssert.Contains(error, "already queued");
        Assert.AreEqual(1, server.GetQueuedPushRequests().Count);
    }

    [TestMethod]
    public async Task PostPush_Returns400_WhenBranchNotFoundLocally()
    {
        var host = new StubJobRepoHost(
            branchExists: _ => Task.FromResult(true),
            branchExistsLocally: _ => Task.FromResult(false));
        await using var server = await StartAsync(host, [new BranchName("rix/ghost")]);

        var response = await PostPushAsync(server, "rix/ghost");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await ReadErrorAsync(response);
        StringAssert.Contains(error, "rix/ghost");
        StringAssert.Contains(error, "working directory");
    }

    [TestMethod]
    public async Task GetPr_Returns200WithEmptyList()
    {
        await using var server = await StartAsync(FakeHost(false));

        var response = await GetAsync(server, "/pr");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(0, (await ReadJsonAsync<List<Dictionary<string, string>>>(response)).Count);
    }

    [TestMethod]
    public async Task GetPr_ReturnsQueuedRequests()
    {
        await using var server = await StartAsync(FakeHost(false));

        await PostPrAsync(server, "rix/feat", title: "Add feature");

        var response = await GetAsync(server, "/pr");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var result = await ReadJsonAsync<List<Dictionary<string, string>>>(response);
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("rix/feat", result[0]["branch"]);
        Assert.AreEqual("main", result[0]["baseBranch"]);
        Assert.AreEqual("Add feature", result[0]["title"]);
        Assert.AreEqual("body", result[0]["body"]);
    }

    [TestMethod]
    public async Task GetPush_ReturnsQueuedRequests()
    {
        await using var server = await StartAsync(FakeHost(true), [new BranchName("rix/feat")]);

        await PostPushAsync(server, "rix/feat");

        var response = await GetAsync(server, "/push");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var result = await ReadJsonAsync<List<Dictionary<string, string>>>(response);
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("rix/feat", result[0]["branch"]);
        Assert.AreEqual("main", result[0]["baseBranch"]);
    }

    [TestMethod]
    public async Task GetPush_ListsPushesInQueuedOrder()
    {
        string[] branches = ["rix/zeta", "rix/alpha", "rix/mid"];
        await using var server = await StartAsync(FakeHost(true), [.. branches.Select(b => new BranchName(b))]);

        foreach (var branch in branches)
            await PostPushAsync(server, branch);

        var listed = await ReadJsonAsync<List<Dictionary<string, string>>>(await GetAsync(server, "/push"));
        CollectionAssert.AreEqual(branches, listed.Select(push => push["branch"]).ToArray());
        CollectionAssert.AreEqual(branches, server.GetQueuedPushRequests().Select(push => push.Branch.Value).ToArray());
    }

    [TestMethod]
    public async Task PostPush_Returns403_ByDefault_WhenNoAllowListConfigured()
    {
        await using var server = await StartAsync(FakeHost(true));

        var response = await PostPushAsync(server, "rix/my-fix");

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        var error = await ReadErrorAsync(response);
        StringAssert.Contains(error, "rix/my-fix");
        StringAssert.Contains(error, "not allowed");
    }

    [TestMethod]
    public async Task PostPush_Returns403_WhenBranchNotAllowed()
    {
        await using var server = await StartAsync(FakeHost(true), [new BranchName("rix/other")]);

        var response = await PostPushAsync(server, "rix/my-fix");

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        var error = await ReadErrorAsync(response);
        StringAssert.Contains(error, "rix/my-fix");
        StringAssert.Contains(error, "not allowed");
        StringAssert.Contains(error, "rix/other");
    }

    [TestMethod]
    public async Task PostPush_DoesNotQueue_WhenBranchNotAllowed()
    {
        await using var server = await StartAsync(FakeHost(true), [new BranchName("rix/other")]);

        var response = await PostPushAsync(server, "rix/my-fix");

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.AreEqual(0, server.GetQueuedPushRequests().Count);
    }

    [TestMethod]
    public async Task PostPush_Returns200_WhenBranchIsAllowed()
    {
        await using var server = await StartAsync(FakeHost(true), [new BranchName("rix/my-fix")]);

        var response = await PostPushAsync(server, "rix/my-fix");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(1, server.GetQueuedPushRequests().Count);
    }

    [TestMethod]
    public async Task PostPush_RejectsAllowedBranch_ThatIsNotInTheAllowList_CaseSensitively()
    {
        await using var server = await StartAsync(FakeHost(true), [new BranchName("rix/My-Fix")]);

        var response = await PostPushAsync(server, "rix/my-fix");

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task PostPr_IsUnaffected_ByAllowedPushBranches()
    {
        await using var server = await StartAsync(FakeHost(false), [new BranchName("rix/other")]);

        var response = await PostPrAsync(server, "rix/my-fix");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(1, server.GetQueuedPrRequests().Count);
    }

    [TestMethod]
    public async Task DeletePr_Returns200_WhenQueued()
    {
        await using var server = await StartAsync(FakeHost(false));

        await PostPrAsync(server, "rix/feat", title: "Add feature");

        var response = await DeleteAsync(server, "/pr", "rix/feat");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("deleted", await ReadStatusAsync(response));
        Assert.AreEqual(0, server.GetQueuedPrRequests().Count);
    }

    [TestMethod]
    public async Task DeletePr_Returns404_WhenNotQueued()
    {
        await using var server = await StartAsync(FakeHost(false));

        var response = await DeleteAsync(server, "/pr", "rix/ghost");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        var error = await ReadErrorAsync(response);
        StringAssert.Contains(error, "rix/ghost");
        StringAssert.Contains(error, "PR");
    }

    [TestMethod]
    public async Task DeletePr_Returns404_ForNonRixBranch_SinceNoneWasEverQueued()
    {
        // DELETE /pr reads the branch as a plain BranchName, not a RixBranchName: the delete handler
        // is shared with /push, which must accept any branch name. /pr's own rix/* invariant still
        // holds in practice, since POST /pr only ever lets a rix/*-named branch into the queue, so a
        // non-rix branch simply can't be found rather than being rejected as malformed.
        await using var server = await StartAsync(FakeHost(false));

        var response = await DeleteAsync(server, "/pr", "main");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task DeletePr_Returns400_ForMissingBranch()
    {
        await using var server = await StartAsync(FakeHost(false));

        var response = await DeleteAsync(server, "/pr", "");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual("branch is required", await ReadErrorAsync(response));
    }

    [TestMethod]
    public async Task DeletePr_RemovesOnlyMatchingBranch()
    {
        await using var server = await StartAsync(FakeHost(false));

        await PostPrAsync(server, "rix/keep", title: "Keep");
        await PostPrAsync(server, "rix/drop", title: "Drop");

        await DeleteAsync(server, "/pr", "rix/drop");

        Assert.AreEqual(1, server.GetQueuedPrRequests().Count);
        Assert.AreEqual(new RixBranchName("rix/keep"), server.GetQueuedPrRequests()[0].Branch);
    }

    [TestMethod]
    public async Task DeletePush_Returns200_WhenQueued()
    {
        await using var server = await StartAsync(FakeHost(true), [new BranchName("rix/feat")]);

        await PostPushAsync(server, "rix/feat");

        var response = await DeleteAsync(server, "/push", "rix/feat");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("deleted", await ReadStatusAsync(response));
        Assert.AreEqual(0, server.GetQueuedPushRequests().Count);
    }

    [TestMethod]
    public async Task DeletePush_Returns404_WhenNotQueued()
    {
        await using var server = await StartAsync(FakeHost(true));

        var response = await DeleteAsync(server, "/push", "rix/ghost");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        var error = await ReadErrorAsync(response);
        StringAssert.Contains(error, "rix/ghost");
        StringAssert.Contains(error, "push");
    }

    [TestMethod]
    public async Task DeletePush_Returns404_ForNonRixBranch_WhenNotQueued()
    {
        await using var server = await StartAsync(FakeHost(true));

        var response = await DeleteAsync(server, "/push", "feature/human-work");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }
}
