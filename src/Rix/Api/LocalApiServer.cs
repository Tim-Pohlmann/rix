using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rix.Repository;

namespace Rix.Api;

internal sealed class LocalApiServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentQueue<PendingPr> _pendingPrRequests;
    private readonly ConcurrentQueue<string> _logs;

    internal Uri BaseUrl { get; }
    internal IReadOnlyList<PendingPr> PendingPrRequests => _pendingPrRequests.ToArray();
    internal IReadOnlyList<string> Logs => _logs.ToArray();

    private LocalApiServer(WebApplication app, Uri baseUrl, ConcurrentQueue<PendingPr> pendingPrRequests, ConcurrentQueue<string> logs)
    {
        _app = app;
        BaseUrl = baseUrl;
        _pendingPrRequests = pendingPrRequests;
        _logs = logs;
    }

    internal static async Task<LocalApiServer> StartAsync(
        IRepositoryHost host,
        CancellationToken cancellationToken)
    {
        var pendingPrRequests = new ConcurrentQueue<PendingPr>();
        var logs = new ConcurrentQueue<string>();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(k => k.Listen(System.Net.IPAddress.Loopback, 0));
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new LogCollector(logs));
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonContext.Default));

        var app = builder.Build();

        MapEndpoints(app, host, pendingPrRequests);

        await app.StartAsync(cancellationToken);

        var baseUrl = new Uri(app.Urls.First());
        return new LocalApiServer(app, baseUrl, pendingPrRequests, logs);
    }

    private static void MapEndpoints(
        WebApplication app,
        IRepositoryHost host,
        ConcurrentQueue<PendingPr> pendingPrRequests)
    {
        app.MapGet("/health", () => Results.Ok());

        app.MapPost("/pr", async (PrRequest req, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Branch)) return Results.BadRequest(new ErrorResponse("branch is required"));
            if (string.IsNullOrWhiteSpace(req.BaseBranch)) return Results.BadRequest(new ErrorResponse("baseBranch is required"));
            if (string.IsNullOrWhiteSpace(req.Title)) return Results.BadRequest(new ErrorResponse("title is required"));
            if (string.IsNullOrWhiteSpace(req.Body)) return Results.BadRequest(new ErrorResponse("body is required"));

            RixBranchName branch;
            try { branch = new RixBranchName(req.Branch); }
            catch (ArgumentException ex) { return Results.BadRequest(new ErrorResponse($"RixBranchName: {ex.Message}")); }

            BranchName baseBranch;
            try { baseBranch = new BranchName(req.BaseBranch); }
            catch (ArgumentException ex) { return Results.BadRequest(new ErrorResponse($"BranchName: {ex.Message}")); }

            PrTitle title;
            try { title = new PrTitle(req.Title); }
            catch (ArgumentException ex) { return Results.BadRequest(new ErrorResponse($"PrTitle: {ex.Message}")); }

            PrBody body;
            try { body = new PrBody(req.Body); }
            catch (ArgumentException ex) { return Results.BadRequest(new ErrorResponse($"PrBody: {ex.Message}")); }

            if (await host.BranchExistsOnRemoteAsync(branch, ct))
                return Results.Conflict(new ErrorResponse($"Branch {branch.Value} already exists on the remote."));

            pendingPrRequests.Enqueue(new PendingPr(branch, baseBranch, title, body));
            return Results.Ok(new PrQueuedResponse("queued"));
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private sealed class LogCollector(ConcurrentQueue<string> logs) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Logger(logs, categoryName);
        public void Dispose() { }

        private sealed class Logger(ConcurrentQueue<string> logs, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                logs.Enqueue($"[{logLevel}] {category}: {formatter(state, exception)}");
        }
    }
}
