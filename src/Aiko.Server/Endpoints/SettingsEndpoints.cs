using Aiko.Application.Contracts;

namespace Aiko.Server.Endpoints;

/// <summary>
/// Settings endpoints: reading and saving project-local settings and the resolved effective view with the
/// source of each value. There is no installation-level document: the defaults a project starts from are
/// the template's, and they are copied into the project at init.
/// </summary>
internal static class SettingsEndpoints
{
    /// <summary>
    /// Maps /api/v1/projects/{projectId}/settings.
    /// </summary>
    public static void MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/api/v1/projects/{projectId}/settings",
            async (
                string projectId,
                IAppSettingsService settings,
                CancellationToken cancellationToken) =>
                TypedResults.Ok(await settings.LoadAsync(projectId, cancellationToken)));
        app.MapPut(
            "/api/v1/projects/{projectId}/settings",
            async (
                string projectId,
                AppSettings request,
                IAppSettingsService settings,
                CancellationToken cancellationToken) =>
            {
                await settings.SaveProjectAsync(projectId, request, cancellationToken);
                return TypedResults.Ok(await settings.LoadAsync(projectId, cancellationToken));
            });
    }
}
