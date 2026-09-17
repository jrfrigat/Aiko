namespace Aiko.Infrastructure.Agents;

/// <summary>
/// Description of an agent configuration file: path, purpose, integration kind
/// and desired content.
/// </summary>
/// <param name="Path">Absolute path of the file.</param>
/// <param name="Description">What installing this file does, for the plan output.</param>
/// <param name="Kind">How the file is merged and removed.</param>
/// <param name="Content">The endpoint for MCP entries, the whole text for owned files.</param>
/// <param name="ServerKey">Key of the Aiko entry inside the MCP servers object.</param>
/// <param name="OwnedMarker">Marker that proves Aiko owns a whole file.</param>
/// <param name="BlockMarkerName">Name of the managed block inside a shared file.</param>
/// <param name="AccessToken">
/// The daemon's access token, written into the MCP entry so the client can authenticate. Without it the
/// daemon answers 401 on <c>/mcp</c> and the agent never sees Aiko.
/// </param>
/// <param name="McpTransport">
/// The client's own type tag for a streamable HTTP server (<c>http</c> for clients that write one).
/// Null when the client infers the transport from the URL.
/// </param>
internal sealed record AgentFileDefinition(
    string Path,
    string Description,
    AgentFileKind Kind,
    string Content,
    string ServerKey = "aiko",
    string OwnedMarker = "<!-- Managed by Aiko -->",
    string BlockMarkerName = "aiko",
    string? AccessToken = null,
    string? McpTransport = null)
{
    /// <summary>
    /// Creates a definition for an HTTP MCP server entry in the root mcpServers object.
    /// </summary>
    public static AgentFileDefinition JsonMcp(
        string path,
        string description,
        string projectMcpEndpoint,
        string? accessToken = null,
        string? mcpTransport = null,
        string serverKey = "aiko") =>
        new(path, description, AgentFileKind.JsonMcp, projectMcpEndpoint, serverKey,
            AccessToken: accessToken, McpTransport: mcpTransport);

    /// <summary>
    /// Creates a definition for an HTTP MCP server entry in the nested mcp.servers object.
    /// </summary>
    public static AgentFileDefinition NestedJsonMcp(
        string path,
        string description,
        string projectMcpEndpoint,
        string? accessToken = null,
        string serverKey = "aiko") =>
        new(path, description, AgentFileKind.NestedJsonMcp, projectMcpEndpoint, serverKey,
            AccessToken: accessToken);
}
