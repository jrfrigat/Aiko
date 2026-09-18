using Aiko.Application.Agents;

namespace Aiko.Infrastructure.Agents;

/// <summary>
/// Cursor adapter: an MCP server in .cursor/mcp.json and an always-apply rule in .cursor/rules.
/// </summary>
public sealed class CursorAgentAdapter : BuiltInAgentAdapter
{
    /// <inheritdoc />
    public override string Id => "cursor";

    /// <inheritdoc />
    public override string DisplayName => "Cursor";

    /// <inheritdoc />
    public override AgentCapabilities Capabilities =>
        AgentCapabilities.McpStdio |
        AgentCapabilities.McpStreamableHttp |
        AgentCapabilities.WorkspaceConfiguration |
        AgentCapabilities.HeadlessLaunch |
        AgentCapabilities.Resume |
        AgentCapabilities.StructuredOutput;

    /// <inheritdoc />
    protected override string[] ExecutableNames => ["cursor-agent", "cursor"];

    /// <summary>
    /// <c>~/.cursor</c>: the editor is installed per user, and its CLI is a separate thing that may be absent.
    /// </summary>
    protected override IReadOnlyList<string> InstallationDirectories => [UserPath(".cursor")];

    /// <inheritdoc />
    private protected override IReadOnlyList<AgentFileDefinition> CreateFiles(
        string projectRoot,
        string projectHandle,
        string projectMcpEndpoint,
        string? accessToken,
        // Cursor has no slash commands, so the card types change nothing here; the argument is part of the
        // shared contract and the rule it writes teaches it to read the context.
        IReadOnlyList<CardTypeDescriptor> cardTypes) =>
    [
        AgentFileDefinition.JsonMcp(
            Path.Combine(projectRoot, ".cursor", "mcp.json"),
            "Merge the project-scoped Aiko HTTP MCP server.",
            projectMcpEndpoint,
            accessToken),
        new(
            Path.Combine(projectRoot, ".cursor", "rules", "aiko.mdc"),
            "Install an always-applied Aiko project rule.",
            AgentFileKind.OwnedText,
            AgentTemplates.CursorRule)
    ];

    private protected override IReadOnlyList<AgentFileDefinition> CreateUserFiles() =>
    [
        new(UserPath(".cursor", "rules", "aiko.mdc"), "Install the global Aiko rule.", AgentFileKind.OwnedText, AgentTemplates.CursorRule)
    ];
}
