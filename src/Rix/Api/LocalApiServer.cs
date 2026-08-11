using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rix.Repository;

namespace Rix.Api;

internal sealed class LocalApiServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly PendingQueue<QueuedPr> _pendingPrRequests;
    private readonly PendingQueue<QueuedPush> _pendingPushRequests;

    internal Uri BaseUrl { get; }
    internal IReadOnlyList<QueuedPr> QueuedPrRequests => _pendingPrRequests.Snapshot();
    internal IReadOnlyList<QueuedPush> QueuedPushRequests => _pendingPushRequests.Snapshot();

    private LocalApiServer
    (
        WebApplication app,
        Uri baseUrl,
        PendingQueue<QueuedPr> pendingPrRequests,
        PendingQueue<QueuedPush> pendingPushRequests
    )
    {
        _app = app;
        BaseUrl = baseUrl;
        _pendingPrRequests = pendingPrRequests;
        _pendingPushRequests = pendingPushRequests;
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
        var pendingPrRequests = new PendingQueue<QueuedPr>();
        var pendingPushRequests = new PendingQueue<QueuedPush>();

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

        MapEndpoints(app, host, cloneDir, pendingPrRequests, pendingPushRequests);

        await app.StartAsync(cancellationToken);

        var baseUrl = new Uri(app.Urls.First());
        return new LocalApiServer(app, baseUrl, pendingPrRequests, pendingPushRequests);
    }

    private static void MapEndpoints
    (
        WebApplication app,
        IRepositoryReadHost host,
        string cloneDir,
        PendingQueue<QueuedPr> pendingPrRequests,
        PendingQueue<QueuedPush> pendingPushRequests
    )
    {
        app.MapGet("/health", () => Results.Ok());
        app.MapPost("/pr", (PrRequest req, CancellationToken ct) => HandlePrAsync(req, host, cloneDir, pendingPrRequests, ct));
        app.MapGet("/pr", () => Results.Ok(pendingPrRequests.Snapshot()));
        app.MapDelete("/pr", ([FromBody] DeleteRequest req) => HandleDeletePr(req, pendingPrRequests));
        app.MapPost("/push", (PushRequest req, CancellationToken ct) => HandlePushAsync(req, host, cloneDir, pendingPushRequests, ct));
        app.MapGet("/push", () => Results.Ok(pendingPushRequests.Snapshot()));
        app.MapDelete("/push", ([FromBody] DeleteRequest req) => HandleDeletePush(req, pendingPushRequests));
    }

    private static async Task<IResult> HandlePrAsync
    (
        PrRequest req,
        IRepositoryReadHost host,
        string cloneDir,
        PendingQueue<QueuedPr> pendingPrRequests,
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

        return Enqueue(pendingPrRequests, queuedPr);
    }

    private static async Task<IResult> HandlePushAsync
    (
        PushRequest req,
        IRepositoryReadHost host,
        string cloneDir,
        PendingQueue<QueuedPush> pendingPushRequests,
        CancellationToken ct
    )
    {
        var validation = req.Validate();
        if (validation is InvalidPush(var reason))
            return Results.BadRequest(new ErrorResponse(reason));
        if (validation is not ValidPush(var queuedPush))
            throw new NotSupportedException($"Unexpected push validation {validation.GetType()}");

        // The point of /push is delivering to a branch that already exists on the remote, so the
        // opposite guard from /pr: if the branch does not exist there, the agent should have used
        // /pr instead.
        if (!await host.BranchExistsOnRemoteAsync(queuedPush.Branch, ct))
            return Results.Conflict(new ErrorResponse($"Branch {queuedPush.Branch.Value} does not exist on the remote. Use /pr to create a new branch."));

        // Same "committed it into your assigned working directory" guard as /pr — a queued push for
        // a branch the agent never actually committed would otherwise fail much later, as an opaque
        // git-bundle failure after the session has ended.
        if (!await host.BranchExistsLocallyAsync(cloneDir, queuedPush.Branch, ct))
            return Results.BadRequest(new ErrorResponse($"Branch {queuedPush.Branch.Value} was not found in your working directory. Make sure you committed it there before calling /push."));

        return Enqueue(pendingPushRequests, queuedPush);
    }

    private static IResult Enqueue<T>(PendingQueue<T> pendingRequests, T item)
    {
        pendingRequests.Enqueue(item);
        return Results.Ok(new QueuedResponse("queued"));
    }

    private static IResult HandleDeletePr(DeleteRequest req, PendingQueue<QueuedPr> pendingPrRequests)
    => HandleDelete(req, pendingPrRequests, "PR");

    private static IResult HandleDeletePush(DeleteRequest req, PendingQueue<QueuedPush> pendingPushRequests)
    => HandleDelete(req, pendingPushRequests, "push");

    /// <summary>Cancels the queued request(s) for <paramref name="req"/>'s branch. Every queued item
    /// for the branch is removed — including same-run duplicates, since any of them would bundle into
    /// the same PR/push. A well-formed branch with nothing queued is a 404 so the agent learns its
    /// cancel was a no-op rather than assuming it took.</summary>
    private static IResult HandleDelete<T>
    (
        DeleteRequest req,
        PendingQueue<T> pendingRequests,
        string kind
    )
        where T : IQueuedRequest
    {
        if (string.IsNullOrWhiteSpace(req.Branch))
            return Results.BadRequest(new ErrorResponse("branch is required"));

        return RixBranchName.Parse(req.Branch) switch
        {
            ParseError<RixBranchName> error => Results.BadRequest(new ErrorResponse($"branch: {error.Error}")),
            ParseSuccess<RixBranchName> branch => RemoveQueued(pendingRequests, branch.Value.Value, kind),
            var other => throw new InvalidOperationException($"Unexpected parse result: {other}"),
        };
    }

    private static IResult RemoveQueued<T>
    (
        PendingQueue<T> pendingRequests,
        string branch,
        string kind
    )
        where T : IQueuedRequest
    {
        if (pendingRequests.RemoveAll(queued => queued.Branch.Value == branch) == 0)
            return Results.NotFound(new ErrorResponse($"No queued {kind} for branch {branch}."));
        return Results.Ok(new QueuedResponse("deleted"));
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
