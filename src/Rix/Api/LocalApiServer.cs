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
    internal static async Task<LocalApiServer> StartAsync(
        IRepositoryHost host,
        CancellationToken cancellationToken,
        Action<string>? logLine = null)
    {
        var pendingPrRequests = new ConcurrentQueue<QueuedPr>();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(k => k.Listen(System.Net.IPAddress.Loopback, 0));
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new LogForwarder(logLine ?? (_ => { })));
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonContext.Default));

        var app = builder.Build();

        MapEndpoints(app, host, pendingPrRequests);

        await app.StartAsync(cancellationToken);

        var baseUrl = new Uri(app.Urls.First());
        return new LocalApiServer(app, baseUrl, pendingPrRequests);
    }

    private static void MapEndpoints(
        WebApplication app,
        IRepositoryHost host,
        ConcurrentQueue<QueuedPr> pendingPrRequests)
    {
        app.MapGet("/health", () => Results.Ok());

        app.MapPost("/pr", async (PrRequest req, CancellationToken ct) =>
            req.Validate() switch
            {
                ValidPr(var queuedPr) when !await host.BranchExistsOnRemoteAsync(queuedPr.Branch, ct)
                    => EnqueuePr(pendingPrRequests, queuedPr),
                ValidPr(var queuedPr)
                    => Results.Conflict(new ErrorResponse($"Branch {queuedPr.Branch.Value} already exists on the remote.")),
                InvalidPr invalid
                    => Results.BadRequest(new ErrorResponse(invalid.Reason)),
                var other
                    => throw new NotSupportedException($"Unexpected PR validation {other.GetType()}"),
            });
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
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                sink($"[{logLevel}] {category}: {formatter(state, exception)}");
        }
    }
}
