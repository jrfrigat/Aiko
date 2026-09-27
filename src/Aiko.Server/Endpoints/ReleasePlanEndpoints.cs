using Aiko.Application.Releases;
using Aiko.Server.Contracts;

namespace Aiko.Server.Endpoints;

/// <summary>
/// The release plan over HTTP: what each version is waiting for, and how far it is from being releasable.
/// </summary>
/// <remarks>
/// Reading only, exactly like the release history beside it. The plan is moved by the agent's own tools
/// (<c>aiko_update_release_plan</c> and <c>aiko_close_release_plan</c>), so a writer here would be a second
/// set of rules to keep in step with those. Nothing is cached either: the readiness is derived from the live
/// cards on every read, which is the whole reason it is worth drawing.
/// </remarks>
internal static class ReleasePlanEndpoints
{
    /// <summary>Maps the release-plan route.</summary>
    public static void MapReleasePlanEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/api/v1/projects/{projectId}/release-plan",
            async Task<IResult> (
                string projectId,
                ReleasePlanReadinessProjector plans,
                CancellationToken cancellationToken) =>
            {
                var report = await plans.ReadAsync(projectId, cancellationToken);
                return Results.Ok(report.Plans.Select(ReleasePlanView.From).ToArray());
            });
    }
}
