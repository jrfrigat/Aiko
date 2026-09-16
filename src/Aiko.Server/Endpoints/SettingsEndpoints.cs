using Aiko.Application.Contracts;

namespace Aiko.Server.Endpoints;

/// <summary>
/// Settings endpoints: reading and saving global application settings and
/// project-local settings, plus the resolved effective view with value sources.
/// </summary>
internal static class SettingsEndpoints
{
    /// <summary>
    /// Maps /api/v1/settings and /api/v1/projects/{projectId}/settings.
    /// </summary>
    public static void MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/api/v1/settings",
            async (IAppSettingsService settings, CancellationToken cancellationToken) =>
                TypedResults.Ok(await settings.LoadAsync(null, cancellationToken)));
        app.MapPut(
            "/api/v1/settings",
            async (
                AppSettings request,
                IAppSettingsService settings,
                CancellationToken cancellationToken) =>
            {
                await settings.SaveGlobalAsync(request, cancellationToken);
                return TypedResults.Ok(await settings.LoadAsync(null, cancellationToken));
            });
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
