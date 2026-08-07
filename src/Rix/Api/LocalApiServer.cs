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
    private readonly ConcurrentQueue<QueuedPush> _pendingPushRequests;
    private readonly ConcurrentQueue<QueuedTaskUpdate> _pendingUpdateRequests;
    private readonly ConcurrentQueue<QueuedTaskRevert> _pendingRevertRequests;

    internal Uri BaseUrl { get; }
    internal IReadOnlyList<QueuedPr> QueuedPrRequests => _pendingPrRequests.ToArray();
    internal IReadOnlyList<QueuedPush> QueuedPushRequests => _pendingPushRequests.ToArray();
    internal IReadOnlyList<QueuedTaskUpdate> QueuedUpdateRequests => _pendingUpdateRequests.ToArray();
    internal IReadOnlyList<QueuedTaskRevert> QueuedRevertRequests => _pendingRevertRequests.ToArray();

    private LocalApiServer
    (
        WebApplication app,
        Uri baseUrl,
        ConcurrentQueue<QueuedPr> pendingPrRequests,
        ConcurrentQueue<QueuedPush> pendingPushRequests,
        ConcurrentQueue<QueuedTaskUpdate> pendingUpdateRequests,
        ConcurrentQueue<QueuedTaskRevert> pendingRevertRequests
    )
    {
        _app = app;
        BaseUrl = baseUrl;
        _pendingPrRequests = pendingPrRequests;
        _pendingPushRequests = pendingPushRequests;
        _pendingUpdateRequests = pendingUpdateRequests;
        _pendingRevertRequests = pendingRevertRequests;
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
        var pendingPushRequests = new ConcurrentQueue<QueuedPush>();
        var pendingUpdateRequests = new ConcurrentQueue<QueuedTaskUpdate>();
        var pendingRevertRequests = new ConcurrentQueue<QueuedTaskRevert>();

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

        MapEndpoints(app, host, cloneDir, pendingPrRequests, pendingPushRequests, pendingUpdateRequests, pendingRevertRequests);

        await app.StartAsync(cancellationToken);

        var baseUrl = new Uri(app.Urls.First());
        return new LocalApiServer
        (
            app,
            baseUrl,
            pendingPrRequests,
            pendingPushRequests,
            pendingUpdateRequests,
            pendingRevertRequests
        );
    }

    private static void MapEndpoints
    (
        WebApplication app,
        IRepositoryReadHost host,
        string cloneDir,
        ConcurrentQueue<QueuedPr> pendingPrRequests,
        ConcurrentQueue<QueuedPush> pendingPushRequests,
        ConcurrentQueue<QueuedTaskUpdate> pendingUpdateRequests,
        ConcurrentQueue<QueuedTaskRevert> pendingRevertRequests
    )
    {
        app.MapGet("/health", () => Results.Ok());
        app.MapGet("/tasks", (CancellationToken ct) => HandleListTasksAsync(host, ct));
        app.MapPost("/pr", (PrRequest req, CancellationToken ct) => HandlePrAsync(req, host, cloneDir, pendingPrRequests, ct));
        app.MapPost("/push", (PushRequest req, CancellationToken ct) => HandlePushAsync(req, host, cloneDir, pendingPushRequests, ct));
        app.MapPost("/tasks/update", (UpdateTaskRequest req, CancellationToken ct) => HandleUpdateTaskAsync(req, host, pendingUpdateRequests, ct));
        app.MapPost("/tasks/revert", (RevertTaskRequest req, CancellationToken ct) => HandleRevertTaskAsync(req, host, pendingRevertRequests, ct));
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

        return Enqueue(pendingPrRequests, queuedPr);
    }

    private static async Task<IResult> HandlePushAsync
    (
        PushRequest req,
        IRepositoryReadHost host,
        string cloneDir,
        ConcurrentQueue<QueuedPush> pendingPushRequests,
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

    /// <summary>Reviews already-submitted tasks: lists the repo's open pull requests whose head
    /// branch follows <c>rix/*</c> — the tasks earlier runs of the factory submitted. Read-only, so
    /// it runs directly against the remote with the job's read credential. A GitHub error is a 400
    /// with the reason, so the agent sees the failure immediately and can retry.</summary>
    private static async Task<IResult> HandleListTasksAsync(IRepositoryReadHost host, CancellationToken ct)
    {
        try
        {
            var prs = await host.ListOpenPullRequestsAsync(ct);
            var tasks = prs.Where(p => p.Branch.StartsWith("rix/", StringComparison.Ordinal)).ToArray();
            return Results.Ok(tasks);
        }
        catch (HttpRequestException ex)
        {
            return Results.BadRequest(new ErrorResponse($"could not list pull requests: {ex.Message}"));
        }
    }

    private static async Task<IResult> HandleUpdateTaskAsync
    (
        UpdateTaskRequest req,
        IRepositoryReadHost host,
        ConcurrentQueue<QueuedTaskUpdate> pendingUpdateRequests,
        CancellationToken ct
    )
    {
        var validation = req.Validate();
        if (validation is InvalidUpdateTask(var reason))
            return Results.BadRequest(new ErrorResponse(reason));
        if (validation is not ValidUpdateTask(var queuedUpdate))
            throw new NotSupportedException($"Unexpected update-task validation {validation.GetType()}");

        // An update needs a task that is already submitted, so the branch must exist on the remote
        // (the same guard /push uses for its own existing-branch deliveries). Unlike /pr and /push
        // there is nothing to bundle, so no local-branch check applies.
        if (!await host.BranchExistsOnRemoteAsync(queuedUpdate.Branch, ct))
            return Results.Conflict(new ErrorResponse($"Branch {queuedUpdate.Branch.Value} does not exist on the remote, so it has no submitted task to update. Submit it with /pr first."));

        return Enqueue(pendingUpdateRequests, queuedUpdate);
    }

    private static async Task<IResult> HandleRevertTaskAsync
    (
        RevertTaskRequest req,
        IRepositoryReadHost host,
        ConcurrentQueue<QueuedTaskRevert> pendingRevertRequests,
        CancellationToken ct
    )
    {
        var validation = req.Validate();
        if (validation is InvalidRevertTask(var reason))
            return Results.BadRequest(new ErrorResponse(reason));
        if (validation is not ValidRevertTask(var queuedRevert))
            throw new NotSupportedException($"Unexpected revert-task validation {validation.GetType()}");

        // Same precondition as /tasks/update: reverting closes an already-submitted task's PR, so
        // the branch must exist on the remote.
        if (!await host.BranchExistsOnRemoteAsync(queuedRevert.Branch, ct))
            return Results.Conflict(new ErrorResponse($"Branch {queuedRevert.Branch.Value} does not exist on the remote, so it has no submitted task to revert. Submit it with /pr first."));

        return Enqueue(pendingRevertRequests, queuedRevert);
    }

    private static IResult Enqueue<T>(ConcurrentQueue<T> pendingRequests, T item)
    {
        pendingRequests.Enqueue(item);
        return Results.Ok(new QueuedResponse("queued"));
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
