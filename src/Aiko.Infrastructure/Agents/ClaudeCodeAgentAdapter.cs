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
        string projectMcpEndpoint)
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
                projectMcpEndpoint),
            new(
                Path.Combine(projectRoot, ".claude", "skills", "aiko", "SKILL.md"),
                "Install the Aiko workflow skill.",
                AgentFileKind.OwnedText,
                AgentTemplates.Skill),
            Command("aiko-story-create", AgentTemplates.StoryCreate),
            Command("aiko-task-create", AgentTemplates.TaskCreate),
            Command("aiko-next-stage", AgentTemplates.NextStage),
            Command("aiko-analyze", AgentTemplates.Analyze),
            Command("aiko-implement", AgentTemplates.Implement),
            Command("aiko-review", AgentTemplates.Review),
            Command("aiko-complete", AgentTemplates.Complete),
            Command("aiko-scope", AgentTemplates.Scope),
            Command("aiko-handoff", AgentTemplates.Handoff),
            Command("aiko-memory", AgentTemplates.Memory),
            Command("aiko-status", AgentTemplates.Status),
            Command("aiko-ui", AgentTemplates.UiCommand)
        ];
    }

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
            Command("aiko-ui", AgentTemplates.GlobalUi)
        ];
    }
}
