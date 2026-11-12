using Aiko.Application.Agents;
using Aiko.Application.Contracts;
using Aiko.Infrastructure.Storage;
using Aiko.Server.Security;

namespace Aiko.Server;

/// <summary>The daemon's own project MCP endpoint, handed to an ACP session.</summary>
internal sealed class DaemonAgentMcpEndpoint(
    IProjectCatalog projects,
    DaemonAccessToken token,
    DaemonEndpointConfiguration endpoint) : IAgentMcpEndpoint
{
    /// <inheritdoc />
    public async ValueTask<AgentMcpEndpoint> GetAsync(
        string projectId,
        string deliveryId,
        CancellationToken cancellationToken)
    {
        var project = await projects.FindAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Unknown Aiko project: {projectId}");
        var settings = await endpoint.TryReadAsync(cancellationToken)
            ?? await endpoint.LoadOrCreateAsync(null, cancellationToken);
        return new AgentMcpEndpoint(
            ProjectMcpEndpoint.For(settings.BaseUri.ToString(), project),
            token.Value);
    }
}
