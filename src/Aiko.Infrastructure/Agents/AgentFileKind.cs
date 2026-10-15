namespace Aiko.Infrastructure.Agents;

/// <summary>
/// How an agent configuration file integrates with Aiko.
/// </summary>
internal enum AgentFileKind
{
    /// <summary>Aiko entry in the root mcpServers object of a JSON file.</summary>
    JsonMcp,

    /// <summary>Aiko entry in the nested mcp.servers object of a JSON file.</summary>
    NestedJsonMcp,

    /// <summary>
    /// Aiko entry in the root <c>mcp</c> object, each entry being the server object itself: the flat shape
    /// OpenCode writes, which holds named servers directly rather than under a <c>servers</c> list.
    /// </summary>
    FlatJsonMcp,

    /// <summary>Managed block between aiko:begin/aiko:end markers.</summary>
    ManagedBlock,

    /// <summary>File fully owned by Aiko and removed on uninstall.</summary>
    OwnedText
}
