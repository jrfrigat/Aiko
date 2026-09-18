using Aiko.Application.Agents;
using Aiko.Application.Contracts;
using Aiko.Server.Contracts;
using Aiko.Server.Security;

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
        // Daemon-level (user-scope) connection: the global /aiko-* skills and commands Aiko writes into the
        // user's home. Project-scoped connection stays under /api/v1/projects/{projectId}/installation,
        // because there the MCP entry is per project.
        app.MapPost(
            "/api/v1/agents/{adapterId}/installation",
            async Task<IResult> (
                string adapterId,
                IUnifiedAgentInstaller installer,
                CancellationToken cancellationToken) =>
            {
                var result = await installer.ApplyUserInstallAsync(adapterId, cancellationToken);
                return result is null
                    ? TypedResults.NotFound()
                    : TypedResults.Ok(await DescribeAsync(installer, adapterId, result, cancellationToken));
            });
        app.MapDelete(
            "/api/v1/agents/{adapterId}/installation",
            async Task<IResult> (
                string adapterId,
                IUnifiedAgentInstaller installer,
                CancellationToken cancellationToken) =>
            {
                var result = await installer.UninstallUserAsync(adapterId, cancellationToken);
                return result is null
                    ? TypedResults.NotFound()
                    : TypedResults.Ok(await DescribeAsync(installer, adapterId, result, cancellationToken));
            });
        app.MapPost(
            "/api/v1/projects/{projectId}/installation-plan",
            async Task<IResult> (
                string projectId,
                PlanAgentInstallationRequest request,
                HttpRequest httpRequest,
                IUnifiedAgentInstaller installer,
                IProjectCatalog catalog,
                DaemonAccessToken accessToken,
                CancellationToken cancellationToken) =>
            {
                if (await catalog.FindAsync(projectId, cancellationToken) is not { } project)
                {
                    return TypedResults.NotFound();
                }

                return TypedResults.Ok(await installer.PlanAsync(
                    project.Id,
                    ProjectMcpEndpoint.For($"{httpRequest.Scheme}://{httpRequest.Host}", project),
                    accessToken.Value,
                    request.SelectedAdapterIds,
                    cancellationToken));
            });
        app.MapPost(
            "/api/v1/projects/{projectId}/installation",
            async Task<IResult> (
                string projectId,
                PlanAgentInstallationRequest request,
                HttpRequest httpRequest,
                IUnifiedAgentInstaller installer,
                IProjectCatalog catalog,
                DaemonAccessToken accessToken,
                CancellationToken cancellationToken) =>
            {
                if (await catalog.FindAsync(projectId, cancellationToken) is not { } project)
                {
                    return TypedResults.NotFound();
                }

                return TypedResults.Ok(await installer.ApplyAsync(
                    project.Id,
                    ProjectMcpEndpoint.For($"{httpRequest.Scheme}://{httpRequest.Host}", project),
                    accessToken.Value,
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

    /// <summary>
    /// Re-reads the adapter after a connect or disconnect, so the answer carries both what the operation
    /// did and where the adapter stands now.
    /// </summary>
    private static async ValueTask<AgentConnectionResponse> DescribeAsync(
        IUnifiedAgentInstaller installer,
        string adapterId,
        AgentInstallationResult result,
        CancellationToken cancellationToken)
    {
        var adapter = (await installer.DiscoverAsync(cancellationToken))
            .First(item => StringComparer.Ordinal.Equals(item.Id, adapterId));
        return new AgentConnectionResponse(adapter, result);
    }
}
