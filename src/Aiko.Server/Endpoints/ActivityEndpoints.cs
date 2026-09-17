using Aiko.Application.Contracts;

namespace Aiko.Server.Endpoints;

/// <summary>
/// Activity endpoints: the per-day series behind the dashboard's contribution graph.
/// </summary>
internal static class ActivityEndpoints
{
    private const int DefaultDays = 365;

    /// <summary>
    /// Maps /api/v1/activity.
    /// </summary>
    public static void MapActivityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/api/v1/activity",
            async (int? days, IActivityReport report, CancellationToken cancellationToken) =>
                TypedResults.Ok(await report.GetActivityAsync(days ?? DefaultDays, cancellationToken)));
    }
}
