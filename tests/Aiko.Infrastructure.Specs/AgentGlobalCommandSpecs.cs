using Aiko.Application.Agents;
using Aiko.Infrastructure.Agents;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// The global <c>/aiko-*</c> commands an adapter installs at user scope. The checks read the plan - what
/// would really be written - rather than a constant, and they exist for the drift STORY-6 was opened on: a
/// command that lands in one adapter's global scope and not in its neighbour's.
/// </summary>
/// <remarks>
/// The global commands are a channel of the two adapters that have one (Claude Code and ZCode); Codex, Cline
/// and Cursor are told about them by the shared Aiko skill or, for Cursor, by the rule channel. That is how
/// the neighbouring commands (<c>/aiko-backup</c>, <c>/aiko-token</c>) already work, so a command is never
/// expected in every adapter - only to be consistent wherever its channel exists.
/// </remarks>
public sealed class AgentGlobalCommandSpecs
{
    [Fact]
    public async Task The_adapters_with_a_command_channel_install_the_same_global_commands()
    {
        var claudeCode = await GlobalCommandsAsync(new ClaudeCodeAgentAdapter());
        var zcode = await GlobalCommandsAsync(new ZCodeAgentAdapter());

        Assert.Equal(claudeCode, zcode);
        Assert.Contains("aiko-logs", claudeCode);
    }

    [Fact]
    public async Task An_adapter_that_installs_global_commands_also_installs_the_skill_that_names_them()
    {
        foreach (var adapter in Adapters())
        {
            var plan = await adapter.PlanUserInstallAsync(CancellationToken.None);

            // The skill is how an agent learns which commands exist - Codex, Cline and Cursor have no
            // per-command files at all. A plan that writes commands without it would leave the agent unable
            // to name them.
            if (plan.Changes.Any(change => IsCommand(change.Path)))
            {
                Assert.Contains(plan.Changes, change => IsGlobalSkill(change.Path));
            }
        }
    }

    private static async Task<string[]> GlobalCommandsAsync(IAgentAdapter adapter)
    {
        var plan = await adapter.PlanUserInstallAsync(CancellationToken.None);
        return plan.Changes
            .Select(change => change.Path)
            .Where(IsCommand)
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsCommand(string path) =>
        string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "commands", StringComparison.Ordinal);

    private static bool IsGlobalSkill(string path) =>
        string.Equals(Path.GetFileName(path), "SKILL.md", StringComparison.Ordinal) &&
        string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "aiko", StringComparison.Ordinal);

    private static IAgentAdapter[] Adapters() =>
    [
        new ClaudeCodeAgentAdapter(),
        new CodexAgentAdapter(),
        new CursorAgentAdapter(),
        new ZCodeAgentAdapter(),
        new ClineAgentAdapter()
    ];
}
