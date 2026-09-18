using Aiko.Application.Agents;

namespace Aiko.Infrastructure.Agents;

/// <summary>
/// Cline adapter.
/// </summary>
/// <remarks>
/// Cline's configuration is split by scope: <c>~/.cline/</c> is shared by every Cline application (IDE,
/// CLI, desktop, SDK) and <c>.cline/</c> belongs to one workspace. Two consequences shape this adapter:
/// <list type="bullet">
/// <item>project-scoped configuration is files only - a project carries its skills
/// (<c>.cline/skills</c>) and its rules (<c>.clinerules</c>), and Cline reads no MCP configuration from a
/// workspace;</item>
/// <item>the MCP endpoint therefore goes into Cline's global files, one entry per project
/// (<c>aiko-&lt;handle&gt;</c>), because a single entry cannot name a project. Cline can enable and disable
/// servers, so several projects can coexist.</item>
/// </list>
/// Hooks and plugins are executable code, so Aiko installs neither anywhere.
/// </remarks>
public sealed class ClineAgentAdapter : BuiltInAgentAdapter
{
    /// <summary>
    /// Directory the Cline applications keep their shared configuration in. The desktop app and the IDE
    /// extension have no executable on PATH, so the directory itself is the sign that Cline is installed.
    /// </summary>
    internal const string ConfigurationDirectoryName = ".cline";

    /// <inheritdoc />
    public override string Id => "cline";

    /// <inheritdoc />
    public override string DisplayName => "Cline";

    /// <inheritdoc />
    public override AgentCapabilities Capabilities =>
        AgentCapabilities.McpStreamableHttp |
        AgentCapabilities.McpStdio |
        AgentCapabilities.WorkspaceConfiguration |
        AgentCapabilities.Skills |
        AgentCapabilities.HeadlessLaunch |
        AgentCapabilities.Resume |
        AgentCapabilities.StructuredOutput;

    /// <inheritdoc />
    protected override string[] ExecutableNames => ["cline"];

    /// <summary>
    /// <c>~/.cline</c>, the configuration directory every Cline application shares. The desktop app and the
    /// IDE extension have nothing on PATH, so the directory is what says the agent is installed at all.
    /// </summary>
    protected override IReadOnlyList<string> InstallationDirectories => [UserPath(ConfigurationDirectoryName)];

    /// <inheritdoc />
    /// <remarks>
    /// The workspace half: the project's working contract, its skills, and the MCP entry. The entry goes
    /// into Cline's global files because a workspace cannot carry one - it is named after the project so
    /// several projects can be connected at once.
    /// <para>
    /// Cline has no slash commands, so the procedures reach it as skills only - one per card type the
    /// project declares, which is why <paramref name="cardTypes"/> is no longer ignorable here.
    /// </para>
    /// </remarks>
    private protected override IReadOnlyList<AgentFileDefinition> CreateFiles(
        string projectRoot,
        string projectHandle,
        string projectMcpEndpoint,
        string? accessToken,
        IReadOnlyList<CardTypeDescriptor> cardTypes)
    {
        var key = ServerKey(projectHandle);
        return
        [
            // The working contract, in the workspace rule channel - the half of the configuration a project
            // can carry.
            new(
                Path.Combine(projectRoot, ".clinerules", "aiko.md"),
                "Add Aiko's working contract as a workspace rule.",
                AgentFileKind.OwnedText,
                AgentTemplates.ProjectInstructions),
            .. AgentTemplates.ProjectProcedures(Id, cardTypes).Select(procedure => new AgentFileDefinition(
                Path.Combine(projectRoot, ".cline", "skills", procedure.Name, "SKILL.md"),
                $"Install the {procedure.Name} skill.",
                AgentFileKind.OwnedText,
                procedure.ToSkill())),
            // The IDE extension and the desktop app read their MCP settings from the data directory...
            AgentFileDefinition.JsonMcp(
                UserPath(ConfigurationDirectoryName, "data", "settings", "cline_mcp_settings.json"),
                "Merge the project's Aiko MCP server into Cline's MCP settings.",
                projectMcpEndpoint,
                accessToken,
                // Cline defaults to the legacy SSE transport when the type is absent, so the streamable
                // HTTP transport has to be spelled out.
                mcpTransport: "streamableHttp",
                serverKey: key),
            // ...and the CLI keeps its own file. Both carry the same definitions.
            AgentFileDefinition.JsonMcp(
                UserPath(ConfigurationDirectoryName, "mcp.json"),
                "Merge the project's Aiko MCP server into the Cline CLI's MCP settings.",
                projectMcpEndpoint,
                accessToken,
                mcpTransport: "streamableHttp",
                serverKey: key)
        ];
    }

    /// <inheritdoc />
    private protected override IReadOnlyList<OwnedDirectory> OwnedDirectories(string projectRoot) =>
    [
        new(Path.Combine(projectRoot, ".cline", "skills"), "SKILL.md", SearchOption.AllDirectories)
    ];

    /// <inheritdoc />
    /// <remarks>
    /// The procedures are installed into the portable <c>~/.agents/skills</c> tree as well, because a Cline
    /// build that does not surface workspace skills - the desktop app reads the global root and leaves
    /// <c>&lt;project&gt;/.cline/skills</c> alone - would never see them otherwise. They are project-agnostic
    /// by design: each reads the project context at run time, so one copy serves every project. A procedure
    /// per card type stays out of the global scope on purpose, because a type is one project's data; the
    /// type-agnostic <c>aiko-create</c> covers it.
    /// </remarks>
    private protected override IReadOnlyList<AgentFileDefinition> CreateUserFiles() =>
    [
        new(
            UserPath(ConfigurationDirectoryName, "skills", "aiko", "SKILL.md"),
            "Install the global Aiko skill in the Cline skills root.",
            AgentFileKind.OwnedText,
            AgentTemplates.GlobalSkill),
        .. AgentTemplates.ProjectProcedures(Id, []).Select(procedure => new AgentFileDefinition(
            UserPath(".agents", "skills", procedure.Name, "SKILL.md"),
            $"Install the {procedure.Name} skill for Cline in the portable agent skills root.",
            AgentFileKind.OwnedText,
            procedure.ToGlobalSkill())),
        // The contract in the rule channel this client reads without a workspace: Cline keeps global rules in
        // ~/.cline/rules, and a rule there holds in every folder rather than only in a project that installed
        // one. The workspace rule stays as well - it is what a project carries for its own team.
        new(
            UserPath(ConfigurationDirectoryName, "rules", "aiko.md"),
            "Install the global Aiko rule in Cline's rules root.",
            AgentFileKind.OwnedText,
            AgentTemplates.ProjectInstructions)
    ];

    /// <inheritdoc />
    protected override IReadOnlyList<string> CreateWarnings() =>
    [
        // The one thing Cline cannot do per project: its MCP servers are global, so each project gets its
        // own entry and the agent decides which one to use.
        "Cline keeps MCP servers globally: this project is added as its own server entry, and Cline enables it per session."
    ];

    /// <summary>
    /// The MCP server key for a project: the project's readable handle under Aiko's prefix, so one entry per
    /// project can coexist in Cline's shared file.
    /// </summary>
    /// <remarks>
    /// The handle rather than the folder name: a folder is called whatever the machine happens to call it,
    /// while the handle is what the project is called, and the key ends up in a file a person reads.
    /// </remarks>
    internal static string ServerKey(string projectHandle) =>
        string.IsNullOrWhiteSpace(projectHandle) ? "aiko" : $"aiko-{projectHandle}";
}
