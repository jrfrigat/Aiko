namespace Aiko.Infrastructure.Agents;

/// <summary>
/// Description of an agent configuration file: path, purpose, integration kind
/// and desired content.
/// </summary>
internal sealed record AgentFileDefinition(
    string Path,
    string Description,
    AgentFileKind Kind,
    string Content,
    string ServerKey = "aiko",
    string OwnedMarker = "<!-- Managed by Aiko -->",
    string BlockMarkerName = "aiko")
{
    /// <summary>
    /// Creates a definition for an HTTP MCP server entry in the root mcpServers object.
    /// </summary>
    public static AgentFileDefinition JsonMcp(
        string path,
        string description,
        string projectMcpEndpoint,
        string serverKey = "aiko") =>
        new(path, description, AgentFileKind.JsonMcp, projectMcpEndpoint, serverKey);

    /// <summary>
    /// Creates a definition for an HTTP MCP server entry in the nested mcp.servers object.
    /// </summary>
    public static AgentFileDefinition NestedJsonMcp(
        string path,
        string description,
        string projectMcpEndpoint,
        string serverKey = "aiko") =>
        new(path, description, AgentFileKind.NestedJsonMcp, projectMcpEndpoint, serverKey);
}
