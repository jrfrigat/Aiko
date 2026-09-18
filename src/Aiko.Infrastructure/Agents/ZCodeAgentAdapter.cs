using Aiko.Application.Agents;

namespace Aiko.Infrastructure.Agents;

/// <summary>
/// ZCode adapter: a native MCP server in .zcode/config.json, a working-contract block in AGENTS.md and a
/// skill plus a slash command per procedure in .zcode.
/// </summary>
/// <remarks>
/// ZCode has no rules file of its own, so the contract travels through the cross-client <c>AGENTS.md</c>
/// mechanism - the same block Codex writes, which is why the two merge into one instead of fighting.
/// </remarks>
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

    /// <summary>
    /// <c>~/.zcode</c>, the client's own data directory. ZCode ships without an executable of its own name on
    /// PATH in the common install, so the directory is what says it is there at all.
    /// </summary>
    protected override IReadOnlyList<string> InstallationDirectories => [UserPath(".zcode")];

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
                Path.Combine(projectRoot, ".zcode", "commands", $"{name}.md"),
                $"Install the /{name} command.",
                AgentFileKind.OwnedText,
                content);

        var procedures = AgentTemplates.ProjectProcedures(Id, cardTypes);

        return
        [
            AgentFileDefinition.NestedJsonMcp(
                Path.Combine(projectRoot, ".zcode", "config.json"),
                "Merge the native project-scoped Aiko HTTP MCP server.",
                projectMcpEndpoint,
                accessToken),
            // The working contract. ZCode reads project instructions from AGENTS.md, and a managed block
            // keeps it beside whatever the project already wrote there.
            new(
                Path.Combine(projectRoot, "AGENTS.md"),
                "Add Aiko's working contract as a project instruction block.",
                AgentFileKind.ManagedBlock,
                AgentTemplates.ProjectInstructions),
            .. procedures.Select(procedure => new AgentFileDefinition(
                Path.Combine(projectRoot, ".zcode", "skills", procedure.Name, "SKILL.md"),
                $"Install the {procedure.Name} skill.",
                AgentFileKind.OwnedText,
                procedure.ToSkill())),
            .. procedures.Select(procedure => Command(procedure.Name, procedure.Body))
        ];
    }

    /// <inheritdoc />
    private protected override IReadOnlyList<OwnedDirectory> OwnedDirectories(string projectRoot) =>
    [
        new(Path.Combine(projectRoot, ".zcode", "commands"), "aiko-*.md"),
        new(Path.Combine(projectRoot, ".zcode", "skills"), "SKILL.md", SearchOption.AllDirectories)
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
            Command("aiko-init", AgentTemplates.InitFor(Id)),
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
