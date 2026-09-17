using Aiko.Application.Agents;

namespace Aiko.Infrastructure.Agents;

/// <summary>
/// Claude Code adapter: .mcp.json, a skill and a slash command in .claude.
/// </summary>
public sealed class ClaudeCodeAgentAdapter : BuiltInAgentAdapter
{
    /// <inheritdoc />
    public override string Id => "claude-code";

    /// <inheritdoc />
    public override string DisplayName => "Claude Code";

    /// <inheritdoc />
    public override AgentCapabilities Capabilities =>
        AgentCapabilities.McpStdio |
        AgentCapabilities.McpStreamableHttp |
        AgentCapabilities.WorkspaceConfiguration |
        AgentCapabilities.Skills |
        AgentCapabilities.Commands |
        AgentCapabilities.Hooks |
        AgentCapabilities.HeadlessLaunch |
        AgentCapabilities.Resume |
        AgentCapabilities.StructuredOutput |
        AgentCapabilities.RateLimitDetection;

    /// <inheritdoc />
    protected override string[] ExecutableNames => ["claude"];

    /// <inheritdoc />
    private protected override IReadOnlyList<AgentFileDefinition> CreateFiles(
        string projectRoot,
        string projectMcpEndpoint,
        string? accessToken,
        IReadOnlyList<CardTypeDescriptor> cardTypes)
    {
        AgentFileDefinition Command(string name, string content) =>
            new(
                Path.Combine(projectRoot, ".claude", "commands", $"{name}.md"),
                $"Install the /{name} command.",
                AgentFileKind.OwnedText,
                content);

        return
        [
            AgentFileDefinition.JsonMcp(
                Path.Combine(projectRoot, ".mcp.json"),
                "Merge the project-scoped Aiko HTTP MCP server.",
                projectMcpEndpoint,
                accessToken,
                // Claude Code tags its own streamable HTTP servers with "http"; matching its output keeps
                // the entry identical to what `claude mcp add` writes.
                mcpTransport: "http"),
            new(
                Path.Combine(projectRoot, ".claude", "skills", "aiko", "SKILL.md"),
                "Install the Aiko workflow skill.",
                AgentFileKind.OwnedText,
                AgentTemplates.Skill),
            Command("aiko-create", AgentTemplates.Create),
            .. cardTypes.Select(type => Command(
                $"aiko-create-{type.Id}",
                AgentTemplates.CreateCard(type))),
            Command("aiko-create-sub", AgentTemplates.CreateSub),
            Command("aiko-estimate", AgentTemplates.Estimate),
            Command("aiko-run", AgentTemplates.Run(Id)),
            Command("aiko-scope", AgentTemplates.Scope),
            Command("aiko-handoff", AgentTemplates.Handoff),
            Command("aiko-memory", AgentTemplates.Memory),
            Command("aiko-status", AgentTemplates.Status),
            Command("aiko-ui", AgentTemplates.UiCommand)
        ];
    }

    /// <inheritdoc />
    private protected override IReadOnlyList<OwnedDirectory> OwnedDirectories(string projectRoot) =>
    [
        new(Path.Combine(projectRoot, ".claude", "commands"), "aiko-*.md")
    ];

    private protected override IReadOnlyList<AgentFileDefinition> CreateUserFiles()
    {
        AgentFileDefinition Command(string name, string content) =>
            new(UserPath(".claude", "commands", $"{name}.md"), $"Install the global /{name} command.", AgentFileKind.OwnedText, content);

        return
        [
            new(UserPath(".claude", "skills", "aiko", "SKILL.md"), "Install the global Aiko skill.", AgentFileKind.OwnedText, AgentTemplates.GlobalSkill),
            Command("aiko-init", AgentTemplates.Init),
            Command("aiko-list-projects", AgentTemplates.ListProjects),
            Command("aiko-status", AgentTemplates.GlobalStatus),
            Command("aiko-doctor", AgentTemplates.GlobalDoctor),
            Command("aiko-repair", AgentTemplates.GlobalRepair),
            Command("aiko-agents", AgentTemplates.GlobalAgents),
            Command("aiko-token", AgentTemplates.GlobalToken),
            Command("aiko-backup", AgentTemplates.GlobalBackup),
            Command("aiko-ui", AgentTemplates.GlobalUi)
        ];
    }
}
