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
    private readonly DeliveryEndpoints _delivery;

    internal Uri BaseUrl { get; }
    internal IReadOnlyList<QueuedPr> GetQueuedPrRequests() => _delivery.PendingPrRequests.Snapshot();
    internal IReadOnlyList<QueuedPush> GetQueuedPushRequests() => _delivery.PendingPushRequests.Snapshot();

    private LocalApiServer(WebApplication app, Uri baseUrl, DeliveryEndpoints delivery)
    {
        _app = app;
        BaseUrl = baseUrl;
        _delivery = delivery;
    }

    /// <param name="logLine">Sink for the server's own diagnostic log lines, forwarded live so they
    /// interleave with the agent's output on the same seam. When <c>null</c>, server logs are dropped.</param>
    /// <param name="cloneDir">The job's own clone of the target repo — the only directory a queued
    /// branch is accepted from, since it's also the directory <c>rix</c> later bundles from.</param>
    /// <param name="allowedPushBranches">The only branches <c>/push</c> may deliver to; when
    /// <c>null</c> or empty, <c>/push</c> rejects every branch — an operator opts in by naming the
    /// branches this run may touch. Any branch name is acceptable here (unlike <c>/pr</c>'s
    /// <c>rix/*</c> requirement), since these already exist on the remote rather than being named
    /// by the agent.</param>
    internal static async Task<LocalApiServer> StartAsync
    (
        IJobRepoHost host,
        string cloneDir,
        CancellationToken cancellationToken,
        Action<string>? logLine = null,
        IReadOnlyList<BranchName>? allowedPushBranches = null
    )
    {
        var allowed = allowedPushBranches ?? [];
        var delivery = new DeliveryEndpoints(host, cloneDir, allowed);

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
            options => options.AddDocumentTransformer(new ApiInfoTransformer(BuildApiDescription(allowed)))
        );

        var app = builder.Build();

        // The one place a request that can't be answered becomes a response, instead of each
        // handler repeating the mapping or letting the exception leak out as an unhandled 500.
        // A malformed field is the caller's mistake, so it is a 400: the handlers construct their
        // value objects straight from the request, and the first field that can't be (blank, or
        // e.g. a /pr branch outside rix/*) throws an InvalidInputException naming it. A failed call
        // to the GitHub API or git (the BranchExists* checks in those same handlers) means the
        // request simply couldn't be judged, which is not the caller's fault, so it is a 502.
        // Both guard on HasStarted: once a handler has begun writing, the status line is already
        // on the wire and overwriting it would throw a second, less useful exception.
        app.Use
        (
            async (context, next) =>
            {
                try
                {
                    await next(context);
                }
                catch (InvalidInputException ex) when (!context.Response.HasStarted)
                {
                    await Results.BadRequest(new ErrorResponse(ex.Message)).ExecuteAsync(context);
                }
                catch (RepoHostException ex) when (!context.Response.HasStarted)
                {
                    await Results.Json
                    (
                        new ErrorResponse($"repository host error: {ex.Message}"),
                        statusCode: StatusCodes.Status502BadGateway
                    )
                    .ExecuteAsync(context);
                }
            }
        );
        app.MapGet("/health", () => Results.Ok())
            .WithTags("meta")
            .WithSummary("Liveness check")
            .WithDescription("Returns 200 once the API is ready to accept requests.");
        delivery.Map(app);
        app.MapOpenApi("/openapi.json");

        await app.StartAsync(cancellationToken);

        var baseUrl = new Uri(app.Urls.First());
        return new LocalApiServer(app, baseUrl, delivery);
    }

    /// <summary>The human-readable overview served as the OpenAPI document's <c>info.description</c> —
    /// the agent reads this instead of a hand-maintained endpoint list in its system prompt, so the
    /// push allow-list for this specific run is folded in here too.</summary>
    private static string BuildApiDescription(IReadOnlyList<BranchName> allowedPushBranches)
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
    private static string BuildPushEndpointDescription(IReadOnlyList<BranchName> allowedPushBranches)
    {
        const string overview =
            "Deliver new commits to a branch that already exists on the remote, for instance when " +
            "resuming a previous run. Commit them locally on that branch first. The branch must exist " +
            "on the remote — use /pr to create a new one. ";

        return overview + PushPolicySentence(allowedPushBranches);
    }

    private static string PushPolicySentence(IReadOnlyList<BranchName> allowedPushBranches)
    => allowedPushBranches.Count switch
    {
        0 => "This run has allowed no push branches, so /push rejects every request; use /pr for all changes.",
        _ => $"This run's /push is restricted to these branches: {FormatBranchList(allowedPushBranches)}.",
    };

    private static string FormatBranchList(IReadOnlyList<BranchName> branches)
    => string.Join(", ", branches.Select(b => b.Value));

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

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    /// <summary>The /pr and /push endpoints, and the requests the agent has queued through them.</summary>
    /// <param name="allowedPushBranches">See <see cref="StartAsync"/>; empty means /push rejects
    /// every branch.</param>
    private sealed class DeliveryEndpoints
    (
        IJobRepoHost host,
        string cloneDir,
        IReadOnlyList<BranchName> allowedPushBranches
    )
    {
        internal PrQueue PendingPrRequests { get; } = new();
        internal BranchQueue<QueuedPush> PendingPushRequests { get; } = new("push", push => push.Branch);

        internal void Map(IEndpointRouteBuilder app)
        {
            // One group so the tag is set once rather than on every endpoint; its empty prefix
            // leaves the routes themselves unchanged.
            var delivery = app.MapGroup("").WithTags("delivery");

            var prDescription =
                "Call this once a rix/<short-description> branch is committed locally in your working " +
                "directory and you are satisfied with it. The branch must not already exist on the remote. " +
                "baseBranch is the branch the PR targets; stacked PRs are allowed as long as the queued base " +
                "branches form no cycle. The pull request is opened after the job ends, not immediately.";

            delivery.MapPost("/pr", (PrRequest req, CancellationToken ct) => HandlePrAsync(req, ct))
                .WithSummary("Queue a branch to be opened as a pull request")
                .WithDescription(prDescription);

            delivery.MapGet("/pr", () => Results.Ok(PendingPrRequests.Snapshot()))
                .WithSummary("List queued pull requests")
                .WithDescription("Returns the pull requests queued so far this run, in the order they will be opened.");

            delivery.MapDelete("/pr", ([FromBody] DeleteRequest req) => HandleDelete(req, PendingPrRequests))
                .WithSummary("Cancel a queued pull request")
                .WithDescription("Removes the queued pull request for the given branch. 404 if nothing is queued for it.");

            delivery.MapPost("/push", (PushRequest req, CancellationToken ct) => HandlePushAsync(req, ct))
                .WithSummary("Queue new commits onto a branch that already exists on the remote")
                .WithDescription(BuildPushEndpointDescription(allowedPushBranches));

            delivery.MapGet("/push", () => Results.Ok(PendingPushRequests.Snapshot()))
                .WithSummary("List queued pushes")
                .WithDescription("Returns the pushes queued so far this run.");

            delivery.MapDelete("/push", ([FromBody] DeleteRequest req) => HandleDelete(req, PendingPushRequests))
                .WithSummary("Cancel a queued push")
                .WithDescription("Removes the queued push for the given branch. 404 if nothing is queued for it.");
        }

        private async Task<IResult> HandlePrAsync(PrRequest req, CancellationToken ct)
        {
            var queuedPr = new QueuedPr
            (
                Input.Required("branch", req.Branch, value => new RixBranchName(value)),
                Input.Required("baseBranch", req.BaseBranch, value => new BranchName(value)),
                Input.Required("title", req.Title, value => new PrTitle(value)),
                Input.Required("body", req.Body, value => new PrBody(value))
            );

            if (await host.BranchExistsOnRemoteAsync(queuedPr.Branch, ct))
                return Results.Conflict(new ErrorResponse($"Branch {queuedPr.Branch.Value} already exists on the remote."));

            return await CheckCommittedLocallyAsync(queuedPr.Branch, "/pr", ct)
                ?? PendingPrRequests.TryEnqueue(queuedPr);
        }

        private async Task<IResult> HandlePushAsync(PushRequest req, CancellationToken ct)
        {
            // Unlike /pr, the branch here already exists on the remote (checked below), so it isn't a
            // name the agent is inventing - any branch name is acceptable, not just rix/*.
            // Named because both are BranchName: transposing them compiles, and would check the push
            // allow-list against the base branch instead of the one being pushed.
            var queuedPush = new QueuedPush
            (
                Branch: Input.Required("branch", req.Branch, value => new BranchName(value)),
                BaseBranch: Input.Required("baseBranch", req.BaseBranch, value => new BranchName(value))
            );

            // The job's configuration names the only branches /push may deliver to (e.g. just the branch
            // this run is resuming); an empty/unset list means none are allowed. Enforced here, before
            // any remote/local checks, so a push the operator never allowed is refused regardless of
            // where the branch lives.
            if (!allowedPushBranches.Contains(queuedPush.Branch))
            {
                var message = allowedPushBranches.Count switch
                {
                    0 => $"Push to branch {queuedPush.Branch.Value} is not allowed. This job does not permit pushing to any branch.",
                    _ => $"Push to branch {queuedPush.Branch.Value} is not allowed. " +
                        $"This job permits pushes only to: {FormatBranchList(allowedPushBranches)}.",
                };
                return Results.Json(new ErrorResponse(message), statusCode: StatusCodes.Status403Forbidden);
            }

            // The point of /push is delivering to a branch that already exists on the remote, so the
            // opposite guard from /pr: if the branch does not exist there, the agent should have used
            // /pr instead.
            if (!await host.BranchExistsOnRemoteAsync(queuedPush.Branch, ct))
                return Results.Conflict(new ErrorResponse($"Branch {queuedPush.Branch.Value} does not exist on the remote. Use /pr to create a new branch."));

            return await CheckCommittedLocallyAsync(queuedPush.Branch, "/push", ct)
                ?? PendingPushRequests.TryEnqueue(queuedPush);
        }

        // Catches an agent that queues a branch it never actually committed into its assigned working
        // directory (e.g. because it made the change somewhere else on the runner) — without this, the
        // mistake surfaces only later, as an opaque git-bundle failure after the agent's session has
        // already ended and it's too late to retry. Returns null when the branch is there.
        private async Task<IResult?> CheckCommittedLocallyAsync(BranchName branch, string endpoint, CancellationToken ct)
        {
            if (await host.BranchExistsLocallyAsync(cloneDir, branch, ct))
                return null;

            var message = $"Branch {branch.Value} was not found in your working directory. " +
                $"Make sure you committed it there (not in a different directory) before calling {endpoint}.";
            return Results.BadRequest(new ErrorResponse(message));
        }

        /// <summary>Cancels the queued request for <paramref name="req"/>'s branch — shared by /pr and
        /// /push. So the branch can't be restricted to rix/* here: only /pr's own POST enforces that
        /// when it queues the branch in the first place; deleting a queued push must accept whatever
        /// name was queued.</summary>
        private static IResult HandleDelete<T>(DeleteRequest req, BranchQueue<T> pendingRequests)
        => pendingRequests.TryRemove(Input.Required("branch", req.Branch, value => new BranchName(value)));
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
