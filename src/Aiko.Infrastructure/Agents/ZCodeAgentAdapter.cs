using Aiko.Application.Agents;

namespace Aiko.Infrastructure.Agents;

/// <summary>
/// ZCode adapter: a native MCP server in .zcode/config.json, a skill and a slash command.
/// </summary>
public sealed class ZCodeAgentAdapter : BuiltInAgentAdapter
{
    /// <inheritdoc />
    public override string Id => "zcode";

    /// <inheritdoc />
    public override string DisplayName => "ZCode";

    /// <inheritdoc />
    public override AgentCapabilities Capabilities =>
        AgentCapabilities.McpStdio |
        AgentCapabilities.McpStreamableHttp |
        AgentCapabilities.WorkspaceConfiguration |
        AgentCapabilities.Skills |
        AgentCapabilities.Commands |
        AgentCapabilities.HeadlessLaunch;

    /// <inheritdoc />
    protected override string[] ExecutableNames => ["zcode"];

    /// <inheritdoc />
    private protected override IReadOnlyList<AgentFileDefinition> CreateFiles(
        string projectRoot,
        string projectMcpEndpoint,
        string? accessToken,
        IReadOnlyList<CardTypeDescriptor> cardTypes)
    {
        AgentFileDefinition Command(string name, string content) =>
            new(
                Path.Combine(projectRoot, ".zcode", "commands", $"{name}.md"),
                $"Install the /{name} command.",
                AgentFileKind.OwnedText,
                content);

        return
        [
            AgentFileDefinition.NestedJsonMcp(
                Path.Combine(projectRoot, ".zcode", "config.json"),
                "Merge the native project-scoped Aiko HTTP MCP server.",
                projectMcpEndpoint,
                accessToken),
            new(
                Path.Combine(projectRoot, ".zcode", "skills", "aiko", "SKILL.md"),
                "Install the Aiko workflow skill.",
                AgentFileKind.OwnedText,
                AgentTemplates.Skill),
            Command("aiko-create", AgentTemplates.Create),
            .. cardTypes.Select(type => Command(
                $"aiko-create-{type.Id}",
                AgentTemplates.CreateCard(type))),
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
        new(Path.Combine(projectRoot, ".zcode", "commands"), "aiko-*.md")
    ];

    /// <inheritdoc />
    protected override IReadOnlyList<string> CreateWarnings() =>
    [
        "Native .zcode/config.json takes precedence over .agents/mcp.json in the same scope."
    ];

    private protected override IReadOnlyList<AgentFileDefinition> CreateUserFiles()
    {
        AgentFileDefinition Command(string name, string content) =>
            new(UserPath(".zcode", "commands", $"{name}.md"), $"Install the global /{name} command.", AgentFileKind.OwnedText, content);

        return
        [
            new(UserPath(".zcode", "skills", "aiko", "SKILL.md"), "Install the global Aiko skill.", AgentFileKind.OwnedText, AgentTemplates.GlobalSkill),
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
