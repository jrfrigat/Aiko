using Aiko.Application.Contracts;
using Aiko.Application.Releases;
using Aiko.Server.Contracts;

namespace Aiko.Server.Endpoints;

/// <summary>
/// The release plan over HTTP: what each version is waiting for, and how far it is from being releasable.
/// </summary>
/// <remarks>
/// Reading, exactly like the release history beside it, plus the one deletion a person needs from the
/// screen: an unshipped plan that will not be released. That route is not a second set of rules - it calls
/// the same store the agent's tools use, so the refusal of a released plan and the carry of its cards are
/// decided in one place. Nothing is cached either: the readiness is derived from the live cards on every
/// read, which is the whole reason it is worth drawing.
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

        app.MapDelete(
            "/api/v1/projects/{projectId}/release-plan/{version}",
            async Task<IResult> (
                string projectId,
                string version,
                string? carryIntoVersion,
                IReleasePlanStore plans,
                CancellationToken cancellationToken) =>
            {
                var deletion = await plans.DeleteAsync(
                    projectId,
                    version,
                    carryIntoVersion,
                    cancellationToken);
                return Results.Ok(deletion);
            });
    }
}
