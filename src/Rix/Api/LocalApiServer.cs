using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rix.Repository;
using System.Collections.Concurrent;

namespace Rix.Api;

internal sealed class LocalApiServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentQueue<QueuedPr> _pendingPrRequests;

    internal Uri BaseUrl { get; }
    internal IReadOnlyList<QueuedPr> QueuedPrRequests => _pendingPrRequests.ToArray();

    private LocalApiServer(WebApplication app, Uri baseUrl, ConcurrentQueue<QueuedPr> pendingPrRequests)
    {
        _app = app;
        BaseUrl = baseUrl;
        _pendingPrRequests = pendingPrRequests;
    }

    /// <param name="logLine">Sink for the server's own diagnostic log lines, forwarded live so they
    /// interleave with the agent's output on the same seam. When <c>null</c>, server logs are dropped.</param>
    /// <param name="cloneDir">The job's own clone of the target repo — the only directory a queued
    /// branch is accepted from, since it's also the directory <c>rix</c> later bundles from.</param>
    internal static async Task<LocalApiServer> StartAsync
    (
        IRepositoryReadHost host,
        string cloneDir,
        CancellationToken cancellationToken,
        Action<string>? logLine = null
    )
    {
        var pendingPrRequests = new ConcurrentQueue<QueuedPr>();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(k => k.Listen(System.Net.IPAddress.Loopback, 0));
        builder.Logging.ClearProviders();
        // With no sink there is nothing to forward to, so skip the provider entirely rather than
        // formatting every line only to drop it.
        if (logLine is not null)
            builder.Logging.AddProvider(new LogForwarder(logLine));
        builder.Services.ConfigureHttpJsonOptions
        (
            options => options.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonContext.Default)
        );

        var app = builder.Build();

        MapEndpoints(app, host, cloneDir, pendingPrRequests);

        await app.StartAsync(cancellationToken);

        var baseUrl = new Uri(app.Urls.First());
        return new LocalApiServer(app, baseUrl, pendingPrRequests);
    }

    private static void MapEndpoints
    (
        WebApplication app,
        IRepositoryReadHost host,
        string cloneDir,
        ConcurrentQueue<QueuedPr> pendingPrRequests
    )
    {
        app.MapGet("/health", () => Results.Ok());
        app.MapPost("/pr", (PrRequest req, CancellationToken ct) => HandlePrAsync(req, host, cloneDir, pendingPrRequests, ct));
    }

    private static async Task<IResult> HandlePrAsync
    (
        PrRequest req,
        IRepositoryReadHost host,
        string cloneDir,
        ConcurrentQueue<QueuedPr> pendingPrRequests,
        CancellationToken ct
    )
    {
        var validation = req.Validate();
        if (validation is InvalidPr(var reason))
            return Results.BadRequest(new ErrorResponse(reason));
        if (validation is not ValidPr(var queuedPr))
            throw new NotSupportedException($"Unexpected PR validation {validation.GetType()}");

        if (await host.BranchExistsOnRemoteAsync(queuedPr.Branch, ct))
            return Results.Conflict(new ErrorResponse($"Branch {queuedPr.Branch.Value} already exists on the remote."));

        // Catches an agent that queues a branch it never actually committed into its assigned
        // working directory (e.g. because it made the change somewhere else on the runner) — without
        // this, the mistake surfaces only later, as an opaque git-bundle failure after the agent's
        // session has already ended and it's too late to retry.
        if (!await host.BranchExistsLocallyAsync(cloneDir, queuedPr.Branch, ct))
        {
            var message = $"Branch {queuedPr.Branch.Value} was not found in your working directory. " +
                "Make sure you committed it there (not in a different directory) before calling /pr.";
            return Results.BadRequest(new ErrorResponse(message));
        }

        return EnqueuePr(pendingPrRequests, queuedPr);
    }

    private static IResult EnqueuePr(ConcurrentQueue<QueuedPr> pendingPrRequests, QueuedPr queuedPr)
    {
        pendingPrRequests.Enqueue(queuedPr);
        return Results.Ok(new PrQueuedResponse("queued"));
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private sealed class LogForwarder(Action<string> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Logger(sink, categoryName);
        public void Dispose() { }

        private sealed class Logger(Action<string> sink, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                // ASP.NET invokes loggers from arbitrary threadpool threads; logging is best-effort,
                // so a throwing sink must never bubble back into request handling.
                try { sink($"[{logLevel}] {category}: {formatter(state, exception)}"); }
                catch { /* drop the line rather than fault the request */ }
            }
        }
    }
}
