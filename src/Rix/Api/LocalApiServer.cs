using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rix.Repository;

namespace Rix.Api;

internal sealed class LocalApiServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    internal Uri BaseUrl { get; }
    internal IReadOnlyList<PullRequest> CreatedPrs { get; }
    internal IReadOnlyList<string> Logs { get; }

    private LocalApiServer(WebApplication app, Uri baseUrl, IReadOnlyList<PullRequest> createdPrs, IReadOnlyList<string> logs)
    {
        _app = app;
        BaseUrl = baseUrl;
        CreatedPrs = createdPrs;
        Logs = logs;
    }

    internal static async Task<LocalApiServer> StartAsync(
        IRepositoryHost repositoryHost,
        CancellationToken cancellationToken)
    {
        var port = FindFreePort();
        var baseUrl = new Uri($"http://127.0.0.1:{port}");
        var createdPrs = new List<PullRequest>();
        var logs = new List<string>();

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(repositoryHost);
        builder.WebHost.UseUrls(baseUrl.ToString());
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new LogCollector(logs));
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonContext.Default));

        var app = builder.Build();
        var server = new LocalApiServer(app, baseUrl, createdPrs, logs);

        MapEndpoints(app, createdPrs);

        await app.StartAsync(cancellationToken);
        return server;
    }

    private static void MapEndpoints(WebApplication app, List<PullRequest> createdPrs)
    {
        app.MapGet("/health", () => Results.Ok());

        app.MapPost("/pr", async (PrRequest req, IRepositoryHost host, CancellationToken ct) =>
        {
            if (req.Validate() is { } error)
                return Results.BadRequest(new ErrorResponse(error));

            var branch = new BranchName(req.Branch);
            if (await host.BranchExistsOnRemoteAsync(branch, ct))
                return Results.Conflict(new ErrorResponse($"Branch {branch.Value} already exists on the remote."));

            await host.PushBranchAsync(branch, ct);
            var url = await host.CreatePullRequestAsync(branch, req.Title, req.Body, req.BaseBranch ?? "main", ct);

            createdPrs.Add(new PullRequest(new Uri(url), branch));
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
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class LogCollector(List<string> logs) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Logger(logs, categoryName);
        public void Dispose() { }

        private sealed class Logger(List<string> logs, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                logs.Add($"[{logLevel}] {category}: {formatter(state, exception)}");
        }
    }
}
