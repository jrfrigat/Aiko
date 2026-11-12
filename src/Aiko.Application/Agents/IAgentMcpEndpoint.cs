namespace Aiko.Application.Agents;

/// <summary>Provides the MCP endpoint and token an ACP session uses to reach Aiko.</summary>
public interface IAgentMcpEndpoint
{
    /// <summary>Returns the endpoint for the project and the delivery that will use it.</summary>
    ValueTask<AgentMcpEndpoint> GetAsync(
        string projectId,
        string deliveryId,
        CancellationToken cancellationToken);

    /// <summary>Revokes the token an endpoint was issued with.</summary>
    ValueTask RevokeAsync(
        AgentMcpEndpoint endpoint,
        CancellationToken cancellationToken);
}

/// <summary>One MCP endpoint handed to an agent session.</summary>
/// <param name="Url">Absolute loopback URL of the project's MCP server.</param>
/// <param name="AccessToken">Token the session authenticates with, or null.</param>
public sealed record AgentMcpEndpoint(string Url, string? AccessToken);
