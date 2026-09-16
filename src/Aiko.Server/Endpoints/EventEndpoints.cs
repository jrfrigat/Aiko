using System.Text;
using System.Text.Json;
using Aiko.Application.Contracts;
using Aiko.Infrastructure.Events;
using Aiko.Server.Contracts;

namespace Aiko.Server.Endpoints;

/// <summary>
/// Event endpoints: the per-project SSE stream (with Last-Event-ID replay) and the
/// paged event history for REST clients.
/// </summary>
internal static class EventEndpoints
{
    private const int ReplayBatchSize = 200;

    /// <summary>
    /// Maps /api/v1/projects/{projectId}/events and .../events/history.
    /// </summary>
    public static void MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/api/v1/projects/{projectId}/events",
            async (
                string projectId,
                HttpContext context,
                IProjectCatalog catalog,
                IAikoEventStore store,
                AikoEventBroadcaster broadcaster) =>
            {
                if (await catalog.FindAsync(projectId, context.RequestAborted) is null)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                var response = context.Response;
                response.StatusCode = StatusCodes.Status200OK;
                response.ContentType = "text/event-stream";
                response.Headers.CacheControl = "no-cache";
                response.Headers.Append("X-Accel-Buffering", "no");
                var cancellationToken = context.RequestAborted;

                using var subscription = broadcaster.Subscribe(projectId);
                try
                {
                    await response.Body.FlushAsync(cancellationToken);

                    // Event ids increase monotonically within a project, so a single watermark
                    // is enough to de-duplicate the replay against the live stream.
                    var lastDeliveredId = -1L;
                    if (long.TryParse(
                            context.Request.Headers["Last-Event-Id"].ToString(),
                            out var lastEventId))
                    {
                        lastDeliveredId = await ReplayAsync(
                            response.Body,
                            store,
                            projectId,
                            lastEventId,
                            cancellationToken);
                    }

                    await foreach (var item in ReadLiveAsync(subscription, lastDeliveredId, cancellationToken))
                    {
                        await WriteEventAsync(response.Body, item, cancellationToken);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // The client disconnected; the stream ends here.
                }
            });

        app.MapGet(
            "/api/v1/projects/{projectId}/events/history",
            async (
                string projectId,
                long after,
                int limit,
                IAikoEventStore store,
                CancellationToken cancellationToken) =>
            {
                var normalizedLimit = Math.Clamp(limit is 0 ? 100 : limit, 1, 500);
                return Results.Ok(await store.ReadAsync(
                    projectId,
                    after,
                    normalizedLimit,
                    cancellationToken));
            });
    }

    private static async Task<long> ReplayAsync(
        Stream body,
        IAikoEventStore store,
        string projectId,
        long afterId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var page = await store.ReadAsync(
                projectId,
                afterId,
                ReplayBatchSize,
                cancellationToken);
            foreach (var item in page)
            {
                await WriteEventAsync(body, item, cancellationToken);
                afterId = item.Id;
            }

            if (page.Count < ReplayBatchSize)
            {
                return afterId;
            }
        }
    }

    private static async IAsyncEnumerable<AikoEvent> ReadLiveAsync(
        IAikoEventSubscription subscription,
        long lastDeliveredId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var reader = subscription.Events;
        while (await reader.WaitToReadAsync(cancellationToken))
        {
            while (reader.TryRead(out var item))
            {
                if (item.Id > lastDeliveredId)
                {
                    lastDeliveredId = item.Id;
                    yield return item;
                }
            }
        }
    }

    private static async Task WriteEventAsync(
        Stream body,
        AikoEvent item,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(item, ServerJsonContext.Default.AikoEvent);
        var frame = $"id: {item.Id}\ndata: {payload}\n\n";
        await body.WriteAsync(Encoding.UTF8.GetBytes(frame), cancellationToken);
        await body.FlushAsync(cancellationToken);
    }
}
