using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rix.Job;
using Rix.Repository;

namespace Rix.Api;

internal sealed class LocalApiServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly List<PrInfo> _createdPrs = [];

    internal string BaseUrl { get; }
    internal IReadOnlyList<PrInfo> CreatedPrs => _createdPrs;

    private LocalApiServer(WebApplication app, string baseUrl)
    {
        _app = app;
        BaseUrl = baseUrl;
    }

    internal static async Task<LocalApiServer> StartAsync(
        IRepositoryHost repositoryHost,
        CancellationToken cancellationToken)
    {
        var port = FindFreePort();
        var baseUrl = $"http://127.0.0.1:{port}";

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(repositoryHost);
        builder.WebHost.UseUrls(baseUrl);
        builder.Logging.ClearProviders();
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonContext.Default));

        var app = builder.Build();
        var server = new LocalApiServer(app, baseUrl);

        server.MapEndpoints();

        await app.StartAsync(cancellationToken);
        return server;
    }

    private void MapEndpoints()
    {
        _app.MapGet("/health", () => Results.Ok());

        _app.MapPost("/pr", async (PrRequest req, IRepositoryHost host, CancellationToken ct) =>
        {
            if (!BranchName.IsValid(req.Branch))
                return Results.BadRequest(new ErrorResponse($"Branch must match rix/* pattern, got: {req.Branch}"));

            if (string.IsNullOrWhiteSpace(req.Title))
                return Results.BadRequest(new ErrorResponse("title is required"));

            var branch = new BranchName(req.Branch);

            var exists = await host.BranchExistsOnRemoteAsync(branch.Value, ct);
            if (exists)
                return Results.Conflict(new ErrorResponse($"Branch {branch.Value} already exists on the remote."));

            await host.PushBranchAsync(branch.Value, ct);
            var url = await host.CreatePullRequestAsync(branch.Value, req.Title, req.Body ?? string.Empty, ct);

            var prInfo = new PrInfo(url, branch.Value);
            _createdPrs.Add(prInfo);

            return Results.Ok(new PrCreatedResponse(url));
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static int FindFreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
