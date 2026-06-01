using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Rix.Api;

namespace Rix.Tests;

[TestClass]
public class LocalApiServerTests
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [TestMethod]
    public async Task GetHealth_Returns200()
    {
        await using var server = await LocalApiServer.StartAsync((_, _) => Task.FromResult(false), CancellationToken.None);
        using var client = new HttpClient();

        var response = await client.GetAsync(new Uri(server.BaseUrl, "/health"));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task PostPr_Returns200WithQueuedStatus()
    {
        await using var server = await LocalApiServer.StartAsync((_, _) => Task.FromResult(false), CancellationToken.None);
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
        await using var server = await LocalApiServer.StartAsync((_, _) => Task.FromResult(false), CancellationToken.None);
        using var client = new HttpClient();

        await client.PostAsJsonAsync(new Uri(server.BaseUrl, "/pr"), new
        {
            branch = "rix/feat",
            title = "Add feature",
            body = "body",
            baseBranch = "main",
        });

        Assert.AreEqual(1, server.PendingPrRequests.Count);
        Assert.AreEqual(new BranchName("rix/feat"), server.PendingPrRequests[0].Branch);
        Assert.AreEqual("Add feature", server.PendingPrRequests[0].Title);
    }

    [TestMethod]
    public async Task PostPr_Returns400_ForNonRixBranch()
    {
        await using var server = await LocalApiServer.StartAsync((_, _) => Task.FromResult(false), CancellationToken.None);
        using var client = new HttpClient();

        var response = await client.PostAsJsonAsync(new Uri(server.BaseUrl, "/pr"), new
        {
            branch = "main",
            title = "Title",
            body = "body",
            baseBranch = "main",
        });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task PostPr_Returns409_WhenBranchAlreadyExists()
    {
        await using var server = await LocalApiServer.StartAsync((_, _) => Task.FromResult(true), CancellationToken.None);
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
}
