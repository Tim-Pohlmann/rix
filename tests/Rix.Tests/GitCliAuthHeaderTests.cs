using Rix.Process;
using Rix.Repository;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Rix.Tests;

/// <summary>Runs the real <c>git</c> against a local HTTP listener, because what is under test is
/// how git combines rix's credential with headers from its own config - a stubbed runner would
/// only echo back what <see cref="GitCli"/> asked for.</summary>
[TestClass]
public class GitCliAuthHeaderTests
{
    /// <summary>The workspace actions/checkout leaves behind persists an <c>extraheader</c> with
    /// the job's token, and rix's work dir sits inside it. Git used to send both headers, which
    /// GitHub rejects as a duplicate.</summary>
    [TestMethod]
    public async Task LsRemote_SendsOnlyRixsAuthorization_InsideACheckoutWithAPersistedHeader()
    {
        var root = Directory.CreateTempSubdirectory("rix-git-auth-").FullName;
        try
        {
            using var server = new TcpListener(IPAddress.Loopback, 0);
            server.Start();
            var host = new Uri($"http://127.0.0.1:{((IPEndPoint)server.LocalEndpoint).Port}/");

            await RunGit(root, "init", "-q", "outer");
            var outer = Path.Combine(root, "outer");
            await RunGit(outer, "config", $"http.{host.GetLeftPart(UriPartial.Authority)}/.extraheader", "AUTHORIZATION: basic persisted");
            var work = Directory.CreateDirectory(Path.Combine(outer, "work")).FullName;

            var request = CaptureFirstRequestAsync(server);

            var git = new GitCli
            (
                host,
                new GitReadToken("rix-token"),
                new ProcessWrapper(),
                new DirectoryPath(work, new LocalFileSystem()),
                new StubLog()
            );
            // The server answers 404, so git fails - only the headers it sent matter here.
            await Assert.ThrowsExactlyAsync<RepoHostException>
            (
                () => git.BranchExistsOnRemoteAsync(new RepoIdentifier("owner/repo"), new BranchName("main"), CancellationToken.None)
            );

            var authorization = (await request)
                .Where(line => line.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var expected = "Authorization: Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("x-access-token:rix-token"));
            CollectionAssert.AreEqual(new[] { expected }, authorization);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Raw TCP rather than HttpListener, which folds repeated headers into one value and
    /// would hide exactly the duplicate this is looking for.</summary>
    private static async Task<List<string>> CaptureFirstRequestAsync(TcpListener server)
    {
        using var client = await server.AcceptTcpClientAsync();
        var stream = client.GetStream();
        var reader = new StreamReader(stream, Encoding.ASCII);
        var lines = new List<string>();
        while (await reader.ReadLineAsync() is { Length: > 0 } line)
            lines.Add(line);
        await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
        return lines;
    }

    private static async Task RunGit(string workingDirectory, params string[] args)
    {
        var result = await new ProcessWrapper().RunAsync
        (
            "git", args, workingDirectory, new Dictionary<string, string>(), null, _ => { }, CancellationToken.None
        );
        Assert.IsInstanceOfType<ProcessSuccess>(result, $"git {string.Join(' ', args)}");
    }
}
