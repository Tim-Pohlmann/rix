using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Rix.Api;
using Rix.Repository;

namespace Rix.Tests;

[TestClass]
public class LocalApiServerTests
{
    private sealed class FakeRepositoryHost : IRepositoryHost
    {
        internal bool BranchExistsResult { get; set; }
        internal string PrUrlResult { get; set; } = "https://github.com/owner/repo/pull/1";
        internal string? LastPushedBranch { get; private set; }
        internal string? LastPrBranch { get; private set; }

        public Task CloneAsync(string targetDirectory, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> BranchExistsOnRemoteAsync(BranchName branch, CancellationToken cancellationToken) =>
            Task.FromResult(BranchExistsResult);
        public Task PushBranchAsync(BranchName branch, CancellationToken cancellationToken)
        {
            LastPushedBranch = branch.Value;
            return Task.CompletedTask;
        }
        public Task<string> CreatePullRequestAsync(BranchName branch, string title, string body, string baseBranch, CancellationToken cancellationToken)
        {
            LastPrBranch = branch.Value;
            return Task.FromResult(PrUrlResult);
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [TestMethod]
    public async Task GetHealth_Returns200()
    {
        var host = new FakeRepositoryHost();
        await using var server = await LocalApiServer.StartAsync(host, CancellationToken.None);
        using var client = new HttpClient();

        var response = await client.GetAsync(new Uri(server.BaseUrl, "/health"));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task PostPr_CreatesAndReturnsPrUrl()
    {
        var host = new FakeRepositoryHost { PrUrlResult = "https://github.com/owner/repo/pull/42" };
        await using var server = await LocalApiServer.StartAsync(host, CancellationToken.None);
        using var client = new HttpClient();

        var response = await client.PostAsJsonAsync(new Uri(server.BaseUrl, "/pr"), new
        {
            branch = "rix/my-fix",
            title = "Fix null ref",
            body = "Fixes the issue",
        });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOpts)!;
        Assert.AreEqual("https://github.com/owner/repo/pull/42", result["url"]);
    }

    [TestMethod]
    public async Task PostPr_RecordsPrInCreatedPrs()
    {
        var host = new FakeRepositoryHost { PrUrlResult = "https://github.com/owner/repo/pull/7" };
        await using var server = await LocalApiServer.StartAsync(host, CancellationToken.None);
        using var client = new HttpClient();

        await client.PostAsJsonAsync(new Uri(server.BaseUrl, "/pr"), new
        {
            branch = "rix/feat",
            title = "Add feature",
            body = "",
        });

        Assert.AreEqual(1, server.CreatedPrs.Count);
        Assert.AreEqual(new BranchName("rix/feat"), server.CreatedPrs[0].Branch);
        Assert.AreEqual(new Uri("https://github.com/owner/repo/pull/7"), server.CreatedPrs[0].Url);
    }

    [TestMethod]
    public async Task PostPr_Returns400_ForNonRixBranch()
    {
        var host = new FakeRepositoryHost();
        await using var server = await LocalApiServer.StartAsync(host, CancellationToken.None);
        using var client = new HttpClient();

        var response = await client.PostAsJsonAsync(new Uri(server.BaseUrl, "/pr"), new
        {
            branch = "main",
            title = "Title",
            body = "",
        });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task PostPr_Returns409_WhenBranchAlreadyExists()
    {
        var host = new FakeRepositoryHost { BranchExistsResult = true };
        await using var server = await LocalApiServer.StartAsync(host, CancellationToken.None);
        using var client = new HttpClient();

        var response = await client.PostAsJsonAsync(new Uri(server.BaseUrl, "/pr"), new
        {
            branch = "rix/existing",
            title = "Title",
            body = "",
        });

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
    }

    [TestMethod]
    public async Task PostPr_PushesBeforeCreatingPr()
    {
        var host = new FakeRepositoryHost();
        await using var server = await LocalApiServer.StartAsync(host, CancellationToken.None);
        using var client = new HttpClient();

        await client.PostAsJsonAsync(new Uri(server.BaseUrl, "/pr"), new
        {
            branch = "rix/order-check",
            title = "Check order",
            body = "",
        });

        Assert.AreEqual("rix/order-check", host.LastPushedBranch);
        Assert.AreEqual("rix/order-check", host.LastPrBranch);
    }
}
