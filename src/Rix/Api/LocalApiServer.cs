using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<string, QueuedPr> _pendingPrRequests;
    private readonly ConcurrentDictionary<string, QueuedPush> _pendingPushRequests;

    internal Uri BaseUrl { get; }
    internal IReadOnlyList<QueuedPr> GetQueuedPrRequests() => _pendingPrRequests.Values.ToArray();
    internal IReadOnlyList<QueuedPush> GetQueuedPushRequests() => _pendingPushRequests.Values.ToArray();

    private LocalApiServer
    (
        WebApplication app,
        Uri baseUrl,
        ConcurrentDictionary<string, QueuedPr> pendingPrRequests,
        ConcurrentDictionary<string, QueuedPush> pendingPushRequests
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
    /// <param name="allowedPushBranches">The only branches <c>/push</c> may deliver to; when
    /// <c>null</c> or empty, <c>/push</c> rejects every branch — an operator opts in by naming the
    /// branches this run may touch.</param>
    internal static async Task<LocalApiServer> StartAsync
    (
        IRepositoryReadHost host,
        string cloneDir,
        CancellationToken cancellationToken,
        Action<string>? logLine = null,
        IReadOnlyList<RixBranchName>? allowedPushBranches = null
    )
    {
        var pendingPrRequests = new ConcurrentDictionary<string, QueuedPr>();
        var pendingPushRequests = new ConcurrentDictionary<string, QueuedPush>();

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

        MapEndpoints(app, host, cloneDir, pendingPrRequests, pendingPushRequests, allowedPushBranches);

        await app.StartAsync(cancellationToken);

        var baseUrl = new Uri(app.Urls.First());
        return new LocalApiServer(app, baseUrl, pendingPrRequests, pendingPushRequests);
    }

    private static void MapEndpoints
    (
        WebApplication app,
        IRepositoryReadHost host,
        string cloneDir,
        ConcurrentDictionary<string, QueuedPr> pendingPrRequests,
        ConcurrentDictionary<string, QueuedPush> pendingPushRequests,
        IReadOnlyList<RixBranchName>? allowedPushBranches
    )
    {
        app.MapGet("/health", () => Results.Ok());
        app.MapPost("/pr", (PrRequest req, CancellationToken ct) => HandlePrAsync(req, host, cloneDir, pendingPrRequests, ct));
        app.MapGet("/pr", () => Results.Ok(pendingPrRequests.Values.ToArray()));
        app.MapDelete("/pr", ([FromBody] DeleteRequest req) => HandleDelete(req, pendingPrRequests, "PR"));
        app.MapPost("/push", (PushRequest req, CancellationToken ct) => HandlePushAsync(req, host, cloneDir, pendingPushRequests, allowedPushBranches, ct));
        app.MapGet("/push", () => Results.Ok(pendingPushRequests.Values.ToArray()));
        app.MapDelete("/push", ([FromBody] DeleteRequest req) => HandleDelete(req, pendingPushRequests, "push"));
    }

    private static async Task<IResult> HandlePrAsync
    (
        PrRequest req,
        IRepositoryReadHost host,
        string cloneDir,
        ConcurrentDictionary<string, QueuedPr> pendingPrRequests,
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

        // Catches a cyclic base-branch chain among queued PRs (e.g. rix/a based on rix/b, rix/b
        // based on rix/a) as early as possible — at submit time this would otherwise surface only
        // after cloning, well after the agent's session (and its chance to fix the queue) has ended.
        // Checked against the hypothetical queue (current values plus this request) before adding
        // anything, so a rejected request never touches pendingPrRequests.
        var candidates = pendingPrRequests.Values.Append(queuedPr).ToList();
        if (PrDependencyOrder.TryOrder(candidates, pr => pr.Branch.Value, pr => pr.BaseBranch.Value) is null)
        {
            return Results.BadRequest
            (
                new ErrorResponse($"Branch {queuedPr.Branch.Value} would create a cyclic base-branch dependency among queued PRs.")
            );
        }

        return Enqueue(pendingPrRequests, queuedPr.Branch.Value, queuedPr);
    }

    private static async Task<IResult> HandlePushAsync
    (
        PushRequest req,
        IRepositoryReadHost host,
        string cloneDir,
        ConcurrentDictionary<string, QueuedPush> pendingPushRequests,
        IReadOnlyList<RixBranchName>? allowedPushBranches,
        CancellationToken ct
    )
    {
        var validation = req.Validate();
        if (validation is InvalidPush(var reason))
            return Results.BadRequest(new ErrorResponse(reason));
        if (validation is not ValidPush(var queuedPush))
            throw new NotSupportedException($"Unexpected push validation {validation.GetType()}");

        // The job's configuration names the only branches /push may deliver to (e.g. just the branch
        // this run is resuming); an empty/unset list means none are allowed. Enforced here, before
        // any remote/local checks, so a push the operator never allowed is refused regardless of
        // where the branch lives.
        var allowed = allowedPushBranches ?? [];
        if (!allowed.Contains(queuedPush.Branch))
        {
            var message = allowed.Count switch
            {
                0 => $"Push to branch {queuedPush.Branch.Value} is not allowed. This job does not permit pushing to any branch.",
                _ => $"Push to branch {queuedPush.Branch.Value} is not allowed. " +
                    $"This job permits pushes only to: {string.Join(", ", allowed.Select(b => b.Value))}.",
            };
            return Results.Json(new ErrorResponse(message), statusCode: StatusCodes.Status403Forbidden);
        }

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

        return Enqueue(pendingPushRequests, queuedPush.Branch.Value, queuedPush);
    }

    // A branch already queued keeps its slot: without this, a second POST for the same branch would
    // report 200 "queued" while silently overwriting the first request, so the caller would have no
    // way to tell its first call never went through.
    private static IResult Enqueue<T>(ConcurrentDictionary<string, T> pendingRequests, string branch, T item)
    {
        if (!pendingRequests.TryAdd(branch, item))
            return Results.Conflict(new ErrorResponse($"Branch {branch} is already queued."));
        return Results.Ok(new QueuedResponse("queued"));
    }

    /// <summary>Cancels the queued request for <paramref name="req"/>'s branch — the dictionary key,
    /// so at most one request per branch can ever be queued at a time. A well-formed branch with
    /// nothing queued is a 404 so the agent learns its cancel was a no-op rather than assuming it
    /// took.</summary>
    private static IResult HandleDelete<T>
    (
        DeleteRequest req,
        ConcurrentDictionary<string, T> pendingRequests,
        string kind
    )
    {
        var validation = req.Validate();
        if (validation is InvalidDelete(var reason))
            return Results.BadRequest(new ErrorResponse(reason));
        if (validation is not ValidDelete(var branch))
            throw new NotSupportedException($"Unexpected delete validation {validation.GetType()}");

        if (pendingRequests.TryRemove(branch.Value, out _))
            return Results.Ok(new QueuedResponse("deleted"));
        return Results.NotFound(new ErrorResponse($"No queued {kind} for branch {branch.Value}."));
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
