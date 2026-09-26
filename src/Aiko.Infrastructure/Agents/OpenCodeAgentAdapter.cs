using Aiko.Application.Agents;

namespace Aiko.Infrastructure.Agents;

/// <summary>
/// OpenCode adapter.
/// </summary>
/// <remarks>
/// OpenCode keeps its MCP servers in a <c>mcp</c> object whose entries carry <c>type: "remote"</c>, a
/// <c>url</c> and <c>headers</c> - the flat shape <see cref="AgentFileKind.FlatJsonMcp"/> describes. Its
/// project file is <c>opencode.json</c> in the workspace and its global one is
/// <c>~/.config/opencode/opencode.json</c>.
/// <para>
/// A global entry cannot name a project, so - as in Cline - each project is added under its own key
/// (<c>aiko-&lt;handle&gt;</c>) pointing at that project's endpoint, and several projects coexist. The working
/// contract travels in the cross-client <c>AGENTS.md</c> block rather than the file's own <c>instructions</c>
/// array: one file has one writer, and a second Aiko entry in the same JSON would fight the MCP one.
/// </para>
/// </remarks>
public sealed class OpenCodeAgentAdapter : BuiltInAgentAdapter
{
    /// <summary>The client's own configuration directory, shared by every OpenCode run of this user.</summary>
    internal const string ConfigurationDirectoryName = "opencode";

    /// <summary>The configuration file OpenCode reads, in both the workspace and the global directory.</summary>
    internal const string ConfigurationFileName = "opencode.json";

    /// <inheritdoc />
    public override string Id => "opencode";

    /// <inheritdoc />
    public override string DisplayName => "OpenCode";

    /// <inheritdoc />
    public override AgentCapabilities Capabilities =>
        AgentCapabilities.McpStreamableHttp |
        AgentCapabilities.WorkspaceConfiguration |
        AgentCapabilities.Commands |
        AgentCapabilities.HeadlessLaunch;

    /// <inheritdoc />
    protected override string[] ExecutableNames => ["opencode"];

    /// <summary>
    /// <c>~/.config/opencode</c>, the client's own data directory, which says OpenCode is present even when
    /// its executable is not on PATH.
    /// </summary>
    protected override IReadOnlyList<string> InstallationDirectories => [GlobalDirectory()];

    /// <inheritdoc />
    private protected override IReadOnlyList<AgentFileDefinition> CreateFiles(
        string projectRoot,
        string projectHandle,
        string projectMcpEndpoint,
        string? accessToken,
        IReadOnlyList<CardTypeDescriptor> cardTypes)
    {
        var procedures = AgentTemplates.ProjectProcedures(Id, cardTypes);
        return
        [
            AgentFileDefinition.FlatJsonMcp(
                Path.Combine(projectRoot, ConfigurationFileName),
                "Merge the project-scoped Aiko HTTP MCP server into OpenCode's mcp object.",
                projectMcpEndpoint,
                accessToken,
                mcpTransport: "remote"),
            // The working contract, in the rule channel every client reads: a managed block keeps it beside
            // whatever the project already wrote in AGENTS.md.
            new(
                Path.Combine(projectRoot, "AGENTS.md"),
                "Add Aiko's working contract as a project instruction block.",
                AgentFileKind.ManagedBlock,
                AgentTemplates.ProjectInstructions),
            // The global file cannot name a project, so each one is added under its own key - the shape Cline
            // already uses - and OpenCode loads the entry it is asked for.
            AgentFileDefinition.FlatJsonMcp(
                Path.Combine(GlobalDirectory(), ConfigurationFileName),
                "Merge the project's Aiko HTTP MCP server into OpenCode's global mcp object.",
                projectMcpEndpoint,
                accessToken,
                mcpTransport: "remote",
                serverKey: ServerKey(projectHandle)),
            .. procedures.Select(procedure => new AgentFileDefinition(
                Path.Combine(projectRoot, ".opencode", "commands", $"{procedure.Name}.md"),
                $"Install the /{procedure.Name} command.",
                AgentFileKind.OwnedText,
                procedure.Body))
        ];
    }

    /// <inheritdoc />
    private protected override IReadOnlyList<OwnedDirectory> OwnedDirectories(string projectRoot) =>
    [
        // The per-card-type commands are generated, so the ones of a type that left the project have to be
        // swept by name.
        new(Path.Combine(projectRoot, ".opencode", "commands"), "aiko-*.md")
    ];

    /// <inheritdoc />
    /// <remarks>
    /// The daemon-level procedures, the ones that belong to no project. They are fixed names, so no sweep is
    /// needed here; the project's own per-type commands are swept through <see cref="OwnedDirectories"/>.
    /// </remarks>
    private protected override IReadOnlyList<AgentFileDefinition> CreateUserFiles() =>
    [
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

    /// <inheritdoc />
    protected override IReadOnlyList<string> CreateWarnings() =>
    [
        // The one thing OpenCode cannot do per project: a global file has no project in it, so each project
        // gets its own entry and the agent loads the one it needs.
        "OpenCode keeps MCP servers globally as well: this project is added under its own key, and OpenCode loads the entry it is asked for."
    ];

    /// <summary>A global <c>/aiko-*</c> command, written into the client's own command directory.</summary>
    private static AgentFileDefinition Command(string name, string content) =>
        new(
            Path.Combine(GlobalDirectory(), "commands", $"{name}.md"),
            $"Install the global /{name} command.",
            AgentFileKind.OwnedText,
            content);

    /// <summary>
    /// <c>~/.config/opencode</c>: the directory the client keeps its configuration in, resolved through the
    /// base helper so a portable install or a spec that moves the home directory sees the change.
    /// </summary>
    private static string GlobalDirectory() => UserPath(".config", ConfigurationDirectoryName);

    /// <summary>
    /// The MCP server key for a project: the project's readable handle under Aiko's prefix, so one entry per
    /// project can coexist in OpenCode's shared file.
    /// </summary>
    internal static string ServerKey(string projectHandle) =>
        string.IsNullOrWhiteSpace(projectHandle) ? "aiko" : $"aiko-{projectHandle}";
}
