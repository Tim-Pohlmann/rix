using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using Rix.Repository;

namespace Rix.Api;

internal sealed class LocalApiServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly PrQueue _pendingPrRequests;
    private readonly ConcurrentDictionary<string, QueuedPush> _pendingPushRequests;

    internal Uri BaseUrl { get; }
    internal IReadOnlyList<QueuedPr> GetQueuedPrRequests() => _pendingPrRequests.Snapshot();
    internal IReadOnlyList<QueuedPush> GetQueuedPushRequests() => _pendingPushRequests.Values.ToArray();

    private LocalApiServer
    (
        WebApplication app,
        Uri baseUrl,
        PrQueue pendingPrRequests,
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
        var pendingPrRequests = new PrQueue();
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

        // The agent learns how to call this API from the served OpenAPI document, not from a
        // hand-maintained list in its system prompt — so the endpoint metadata below (summaries,
        // descriptions, request shapes) is the single source of truth the agent actually reads.
        builder.Services.AddOpenApi
        (
            options => options.AddDocumentTransformer(new ApiInfoTransformer(BuildApiDescription(allowedPushBranches)))
        );

        var app = builder.Build();

        MapEndpoints(app, host, cloneDir, pendingPrRequests, pendingPushRequests, allowedPushBranches);
        app.MapOpenApi("/openapi.json");

        await app.StartAsync(cancellationToken);

        var baseUrl = new Uri(app.Urls.First());
        return new LocalApiServer(app, baseUrl, pendingPrRequests, pendingPushRequests);
    }

    private static void MapEndpoints
    (
        WebApplication app,
        IRepositoryReadHost host,
        string cloneDir,
        PrQueue pendingPrRequests,
        ConcurrentDictionary<string, QueuedPush> pendingPushRequests,
        IReadOnlyList<RixBranchName>? allowedPushBranches
    )
    {
        const string deliveryTag = "delivery";

        var prDescription =
            "Call this once a rix/<short-description> branch is committed locally in your working " +
            "directory and you are satisfied with it. The branch must not already exist on the remote. " +
            "baseBranch is the branch the PR targets; stacked PRs are allowed as long as the queued base " +
            "branches form no cycle. The pull request is opened after the job ends, not immediately.";

        app.MapGet("/health", () => Results.Ok())
            .WithTags("meta")
            .WithSummary("Liveness check")
            .WithDescription("Returns 200 once the API is ready to accept requests.");

        app.MapPost("/pr", (PrRequest req, CancellationToken ct) => HandlePrAsync(req, host, cloneDir, pendingPrRequests, ct))
            .WithTags(deliveryTag)
            .WithSummary("Queue a branch to be opened as a pull request")
            .WithDescription(prDescription);

        app.MapGet("/pr", () => Results.Ok(pendingPrRequests.Snapshot()))
            .WithTags(deliveryTag)
            .WithSummary("List queued pull requests")
            .WithDescription("Returns the pull requests queued so far this run, in the order they will be opened.");

        app.MapDelete("/pr", ([FromBody] DeleteRequest req) => HandleDelete(req, pendingPrRequests.TryRemove))
            .WithTags(deliveryTag)
            .WithSummary("Cancel a queued pull request")
            .WithDescription("Removes the queued pull request for the given branch. 404 if nothing is queued for it.");

        app.MapPost("/push", (PushRequest req, CancellationToken ct) => HandlePushAsync(req, host, cloneDir, pendingPushRequests, allowedPushBranches, ct))
            .WithTags(deliveryTag)
            .WithSummary("Queue new commits onto a branch that already exists on the remote")
            .WithDescription(BuildPushEndpointDescription(allowedPushBranches));

        app.MapGet("/push", () => Results.Ok(pendingPushRequests.Values.ToArray()))
            .WithTags(deliveryTag)
            .WithSummary("List queued pushes")
            .WithDescription("Returns the pushes queued so far this run.");

        app.MapDelete("/push", ([FromBody] DeleteRequest req) => HandleDelete(req, branch => RemoveFromDictionary(pendingPushRequests, branch)))
            .WithTags(deliveryTag)
            .WithSummary("Cancel a queued push")
            .WithDescription("Removes the queued push for the given branch. 404 if nothing is queued for it.");
    }

    /// <summary>The human-readable overview served as the OpenAPI document's <c>info.description</c> —
    /// the agent reads this instead of a hand-maintained endpoint list in its system prompt, so the
    /// push allow-list for this specific run is folded in here too.</summary>
    private static string BuildApiDescription(IReadOnlyList<RixBranchName>? allowedPushBranches)
    {
        const string overview =
            "Local delivery API for a `rix job` coding-agent run. Hand finished work back to rix by " +
            "queuing a branch: POST /pr opens it as a pull request, POST /push adds commits to a branch " +
            "that already exists on the remote. Queued requests are listed with GET and cancelled with " +
            "DELETE on the same path, and nothing is delivered until the job ends.\n\n" +
            "Conventions: work on branches named rix/<short-description> and commit locally before " +
            "queuing them; split unrelated changes into separate pull requests.";

        return overview + "\n\n" + PushPolicySentence(allowedPushBranches);
    }

    /// <summary>The <c>/push</c> endpoint description, including this run's allow-list so the agent
    /// sees what <c>/push</c> will accept without having to trigger a rejection first.</summary>
    private static string BuildPushEndpointDescription(IReadOnlyList<RixBranchName>? allowedPushBranches)
    {
        const string overview =
            "Deliver new commits to a branch that already exists on the remote, for instance when " +
            "resuming a previous run. Commit them locally on that branch first. The branch must exist " +
            "on the remote — use /pr to create a new one. ";

        return overview + PushPolicySentence(allowedPushBranches);
    }

    private static string PushPolicySentence(IReadOnlyList<RixBranchName>? allowedPushBranches)
    {
        var allowed = allowedPushBranches ?? [];
        return allowed.Count switch
        {
            0 => "This run has allowed no push branches, so /push rejects every request; use /pr for all changes.",
            _ => $"This run's /push is restricted to these branches: {string.Join(", ", allowed.Select(b => b.Value))}.",
        };
    }

    private sealed class ApiInfoTransformer(string description) : IOpenApiDocumentTransformer
    {
        public Task TransformAsync
        (
            OpenApiDocument document,
            OpenApiDocumentTransformerContext context,
            CancellationToken cancellationToken
        )
        {
            document.Info.Title = "rix job delivery API";
            document.Info.Description = description;
            return Task.CompletedTask;
        }
    }

    private static async Task<IResult> HandlePrAsync
    (
        PrRequest req,
        IRepositoryReadHost host,
        string cloneDir,
        PrQueue pendingPrRequests,
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

        return pendingPrRequests.TryEnqueue(queuedPr);
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

    /// <summary>Cancels the queued request for <paramref name="req"/>'s branch by dispatching to
    /// <paramref name="remove"/> once the branch is known well-formed — shared by /pr and /push,
    /// which differ only in where the branch is actually removed from.</summary>
    private static IResult HandleDelete(DeleteRequest req, Func<RixBranchName, IResult> remove)
    {
        var validation = req.Validate();
        if (validation is InvalidDelete(var reason))
            return Results.BadRequest(new ErrorResponse(reason));
        if (validation is not ValidDelete(var branch))
            throw new NotSupportedException($"Unexpected delete validation {validation.GetType()}");

        return remove(branch);
    }

    // A well-formed branch with nothing queued is a 404 so the agent learns its cancel was a no-op
    // rather than assuming it took.
    private static IResult RemoveFromDictionary(ConcurrentDictionary<string, QueuedPush> pendingRequests, RixBranchName branch)
    {
        if (pendingRequests.TryRemove(branch.Value, out _))
            return Results.Ok(new QueuedResponse("deleted"));
        return Results.NotFound(new ErrorResponse($"No queued push for branch {branch.Value}."));
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
