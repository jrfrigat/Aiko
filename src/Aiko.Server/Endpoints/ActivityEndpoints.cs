using Aiko.Application.Contracts;

namespace Aiko.Server.Endpoints;

/// <summary>
/// Activity endpoints: the per-day series behind the dashboard's contribution graph, and the same
/// series narrowed to one project for the project page's own calendar.
/// </summary>
internal static class ActivityEndpoints
{
    private const int DefaultDays = 365;

    /// <summary>
    /// Maps /api/v1/activity and /api/v1/projects/{projectId}/activity.
    /// </summary>
    public static void MapActivityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/api/v1/activity",
            async (int? days, IActivityReport report, CancellationToken cancellationToken) =>
                TypedResults.Ok(await report.GetActivityAsync(days ?? DefaultDays, cancellationToken)));

        // The project's own days, addressed the way the UI addresses a project - by its readable handle.
        // An unknown project is a 404 through the exception mapping, like every other project route.
        app.MapGet(
            "/api/v1/projects/{projectId}/activity",
            async (
                string projectId,
                int? days,
                IActivityReport report,
                CancellationToken cancellationToken) =>
                TypedResults.Ok(await report.GetProjectActivityAsync(
                    projectId,
                    days ?? DefaultDays,
                    cancellationToken)));
    }
}
