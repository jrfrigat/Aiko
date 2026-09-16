using Aiko.Server.Contracts;

namespace Aiko.Server.Endpoints;

/// <summary>
/// System endpoints: the health check and daemon information.
/// </summary>
internal static class SystemEndpoints
{
    /// <summary>
    /// Maps /health and /api/v1/system.
    /// </summary>
    /// <param name="app">The endpoint route builder.</param>
    /// <param name="baseUri">The loopback base URL the daemon listens on.</param>
    public static void MapSystemEndpoints(this IEndpointRouteBuilder app, Uri baseUri)
    {
        app.MapGet("/health", () => TypedResults.Ok(new HealthResponse("healthy")));
        app.MapGet("/api/v1/system", () => TypedResults.Ok(new SystemResponse(
            "Aiko",
            "0.1.0-dev",
            Environment.ProcessId,
            baseUri.ToString().TrimEnd('/'),
            DateTimeOffset.UtcNow)));
    }
}
