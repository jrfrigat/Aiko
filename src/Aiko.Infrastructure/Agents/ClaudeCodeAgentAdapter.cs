using Aiko.Application.Agents;

namespace Aiko.Infrastructure.Agents;

/// <summary>
/// Claude Code adapter: .mcp.json, CLAUDE.md, a skill and a slash command per procedure in .claude.
/// </summary>
/// <remarks>
/// The working contract goes into <c>CLAUDE.md</c> as a managed block - Claude Code's project memory is
/// read whether or not a model decides to load anything, which is what a contract needs, and it is the
/// user's own file, so the block is upserted rather than the file owned. The skills carry the procedures.
/// </remarks>
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

    /// <summary>
    /// <c>~/.claude</c>, which exists for the IDE extension and the desktop app as well as for the CLI.
    /// </summary>
    protected override IReadOnlyList<string> InstallationDirectories => [UserPath(".claude")];

    /// <inheritdoc />
    private protected override IReadOnlyList<AgentFileDefinition> CreateFiles(
        string projectRoot,
        string projectHandle,
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

        // One procedure, two channels: the skill is what a model loads by relevance, the command is what a
        // person types. Both carry the same body, so they cannot drift apart.
        var procedures = AgentTemplates.ProjectProcedures(Id, cardTypes);

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
            // The working contract, in the only place Claude Code reads unconditionally. A managed block,
            // because CLAUDE.md is the user's own project memory.
            new(
                Path.Combine(projectRoot, "CLAUDE.md"),
                "Add Aiko's working contract as a project instruction block.",
                AgentFileKind.ManagedBlock,
                AgentTemplates.ProjectInstructions),
            .. procedures.Select(procedure => new AgentFileDefinition(
                Path.Combine(projectRoot, ".claude", "skills", procedure.Name, "SKILL.md"),
                $"Install the {procedure.Name} skill.",
                AgentFileKind.OwnedText,
                procedure.ToSkill())),
            .. procedures.Select(procedure => Command(procedure.Name, procedure.Body))
        ];
    }

    /// <inheritdoc />
    private protected override IReadOnlyList<OwnedDirectory> OwnedDirectories(string projectRoot) =>
    [
        new(Path.Combine(projectRoot, ".claude", "commands"), "aiko-*.md"),
        // Every skill is its own directory, so the sweep has to descend into the skills root.
        new(Path.Combine(projectRoot, ".claude", "skills"), "SKILL.md", SearchOption.AllDirectories)
    ];

    private protected override IReadOnlyList<AgentFileDefinition> CreateUserFiles()
    {
        AgentFileDefinition Command(string name, string content) =>
            new(UserPath(".claude", "commands", $"{name}.md"), $"Install the global /{name} command.", AgentFileKind.OwnedText, content);

        return
        [
            new(UserPath(".claude", "skills", "aiko", "SKILL.md"), "Install the global Aiko skill.", AgentFileKind.OwnedText, AgentTemplates.GlobalSkill),
            Command("aiko-init", AgentTemplates.InitFor(Id)),
            Command("aiko-list-projects", AgentTemplates.ListProjects),
            Command("aiko-status", AgentTemplates.GlobalStatus),
            Command("aiko-doctor", AgentTemplates.GlobalDoctor),
            Command("aiko-repair", AgentTemplates.GlobalRepair),
            Command("aiko-agents", AgentTemplates.GlobalAgents),
            Command("aiko-settings", AgentTemplates.GlobalSettings),
            Command("aiko-token", AgentTemplates.GlobalToken),
            Command("aiko-release", AgentTemplates.GlobalRelease),
            Command("aiko-backup", AgentTemplates.GlobalBackup),
            Command("aiko-logs", AgentTemplates.GlobalLogs),
            Command("aiko-ui", AgentTemplates.GlobalUi)
        ];
    }
}
