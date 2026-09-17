using Aiko.Application.Agents;

namespace Aiko.Infrastructure.Agents;

/// <summary>
/// Codex adapter: a TOML configuration in .codex, a skill in .codex/skills (the client's own
/// skills root) and in the portable .agents/skills, and a managed block in AGENTS.md.
/// </summary>
public sealed class CodexAgentAdapter : BuiltInAgentAdapter
{
    /// <inheritdoc />
    public override string Id => "codex";

    /// <inheritdoc />
    public override string DisplayName => "Codex";

    /// <inheritdoc />
    public override AgentCapabilities Capabilities =>
        AgentCapabilities.McpStdio |
        AgentCapabilities.McpStreamableHttp |
        AgentCapabilities.WorkspaceConfiguration |
        AgentCapabilities.Skills |
        AgentCapabilities.Commands |
        AgentCapabilities.HeadlessLaunch |
        AgentCapabilities.Resume |
        AgentCapabilities.StructuredOutput |
        AgentCapabilities.RateLimitDetection |
        AgentCapabilities.RemoteWorkspace;

    /// <inheritdoc />
    protected override string[] ExecutableNames => ["codex"];

    /// <inheritdoc />
    private protected override IReadOnlyList<AgentFileDefinition> CreateFiles(
        string projectRoot,
        string projectMcpEndpoint) =>
    [
        new(
            Path.Combine(projectRoot, ".codex", "config.toml"),
            "Merge mcp_servers.aiko with the project-scoped HTTP endpoint.",
            AgentFileKind.ManagedBlock,
            $$"""
            [mcp_servers.aiko]
            url = "{{projectMcpEndpoint}}"
            """),
        new(
            Path.Combine(projectRoot, ".agents", "skills", "aiko", "SKILL.md"),
            "Install the repository-scoped Aiko skill.",
            AgentFileKind.OwnedText,
            AgentTemplates.Skill),
        new(
            Path.Combine(projectRoot, "AGENTS.md"),
            "Add a marked Aiko instruction block without replacing project instructions.",
            AgentFileKind.ManagedBlock,
            AgentTemplates.ProjectInstructions)
    ];

    /// <inheritdoc />
    protected override IReadOnlyList<string> CreateWarnings() =>
    [
        "Project-scoped .codex configuration is ignored while the project is untrusted."
    ];

    private protected override IReadOnlyList<AgentFileDefinition> CreateUserFiles() =>
    [
        // The client's OWN skills root. Codex (CLI and the desktop app) keeps user-scope skills in
        // ~/.codex/skills - its built-ins live in ~/.codex/skills/.system - so the global Aiko skill
        // has to be here to be loaded at all.
        new(
            UserPath(".codex", "skills", "aiko", "SKILL.md"),
            "Install the global Aiko skill in the Codex skills root.",
            AgentFileKind.OwnedText,
            AgentTemplates.GlobalSkill),
        // The portable location stays: it is the cross-client convention the docs describe, and a
        // layout that reads it (or a future client that does) keeps working. Both files carry the
        // same template, and uninstall removes both because it walks this same list.
        new(
            UserPath(".agents", "skills", "aiko", "SKILL.md"),
            "Install the global Aiko skill in the portable agent skills root.",
            AgentFileKind.OwnedText,
            AgentTemplates.GlobalSkill)
    ];
}
