using Aiko.Application.Agents;
using Aiko.Application.Contracts;
using Aiko.Server.Contracts;

namespace Aiko.Server.Endpoints;

/// <summary>
/// Agent endpoints: adapter discovery and planning/applying installations.
/// </summary>
internal static class AgentEndpoints
{
    /// <summary>
    /// Maps /api/v1/agents and the installation/uninstallation routes.
    /// </summary>
    public static void MapAgentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/api/v1/agents",
            async (IUnifiedAgentInstaller installer, CancellationToken cancellationToken) =>
                TypedResults.Ok(await installer.DiscoverAsync(cancellationToken)));
        app.MapPost(
            "/api/v1/projects/{projectId}/installation-plan",
            async (
                string projectId,
                PlanAgentInstallationRequest request,
                HttpRequest httpRequest,
                IUnifiedAgentInstaller installer,
                CancellationToken cancellationToken) =>
            {
                var endpoint =
                    $"{httpRequest.Scheme}://{httpRequest.Host}/mcp/projects/{Uri.EscapeDataString(projectId)}";
                return TypedResults.Ok(await installer.PlanAsync(
                    projectId,
                    endpoint,
                    request.SelectedAdapterIds,
                    cancellationToken));
            });
        app.MapPost(
            "/api/v1/projects/{projectId}/installation",
            async (
                string projectId,
                PlanAgentInstallationRequest request,
                HttpRequest httpRequest,
                IUnifiedAgentInstaller installer,
                CancellationToken cancellationToken) =>
            {
                var endpoint =
                    $"{httpRequest.Scheme}://{httpRequest.Host}/mcp/projects/{Uri.EscapeDataString(projectId)}";
                return TypedResults.Ok(await installer.ApplyAsync(
                    projectId,
                    endpoint,
                    request.SelectedAdapterIds,
                    cancellationToken));
            });
        app.MapPost(
            "/api/v1/projects/{projectId}/uninstallation-plan",
            async (
                string projectId,
                PlanAgentInstallationRequest request,
                IUnifiedAgentInstaller installer,
                CancellationToken cancellationToken) =>
                TypedResults.Ok(await installer.PlanUninstallAsync(
                    projectId,
                    request.SelectedAdapterIds,
                    cancellationToken)));
        app.MapPost(
            "/api/v1/projects/{projectId}/uninstallation",
            async (
                string projectId,
                PlanAgentInstallationRequest request,
                IUnifiedAgentInstaller installer,
                CancellationToken cancellationToken) =>
                TypedResults.Ok(await installer.UninstallAsync(
                    projectId,
                    request.SelectedAdapterIds,
                    cancellationToken)));
    }
}
