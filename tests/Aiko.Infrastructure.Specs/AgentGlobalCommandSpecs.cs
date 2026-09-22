using Aiko.Application.Agents;
using Aiko.Application.Contracts;
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

    [Fact]
    public async Task The_release_command_reaches_every_channel_that_carries_the_other_global_commands()
    {
        // The command channel is only two adapters wide, so a command that landed there alone would be
        // invisible to the rest: Codex, Cline and Cursor read the list out of the shared skill.
        var claudeCode = await GlobalCommandsAsync(new ClaudeCodeAgentAdapter());
        Assert.Contains("aiko-release", claudeCode);
        Assert.Contains("/aiko-release", AgentTemplates.GlobalSkill, StringComparison.Ordinal);
    }

    [Fact]
    public void The_release_procedure_takes_its_order_from_the_projects_scheme_and_names_the_boundary()
    {
        var body = AgentTemplates.GlobalRelease;

        // The order of a release is the project's own, and the procedure points at it instead of carrying a
        // copy of it: two descriptions of one order are two descriptions that drift apart.
        Assert.Contains("aiko_get_project_context", body, StringComparison.Ordinal);
        Assert.Contains("section `release`", body, StringComparison.Ordinal);
        Assert.Contains("aiko_list_releases", body, StringComparison.Ordinal);
        Assert.Contains("aiko_record_release", body, StringComparison.Ordinal);
        // Which tree the tag is made in stays the procedure's own business: no scheme can check that.
        Assert.Contains("aiko project find", body, StringComparison.Ordinal);

        // The boundary is the point of the procedure, not a footnote to it, and it is said in words - silence
        // about the push reads as permission.
        Assert.Contains("Aiko does not push the tag", body, StringComparison.Ordinal);
        Assert.Contains("Aiko installs nothing on your machine", body, StringComparison.Ordinal);
        Assert.Contains("aiko agent install --scope user", body, StringComparison.Ordinal);

        // And none of a scheme's own steps is repeated here. The check reads the schemes Aiko ships rather than
        // a phrase copied into this spec, and the first step of each stands for its text.
        foreach (var scheme in ReleaseSchemes.BuiltIn)
        {
            var firstStep = scheme.Body
                .Split('\n')
                .First(line => line.TrimStart().StartsWith("1.", StringComparison.Ordinal))
                .Trim();
            Assert.DoesNotContain(firstStep, body, StringComparison.Ordinal);
        }

        // Nor the version shape: choosing a version is a step of the scheme, not of the procedure.
        Assert.DoesNotContain("v<major>.<minor>.<patch>", body, StringComparison.Ordinal);
    }

    [Fact]
    public void The_release_procedure_takes_the_policy_as_a_parameter()
    {
        var body = AgentTemplates.GlobalRelease;

        // The command names the policy the release follows, and the same id arrives when the release screen
        // placed the request - it travels in the command's text - so one input serves both a person typing the
        // command and a dialog queueing it.
        Assert.Contains("/aiko-release <scheme-id>", body, StringComparison.Ordinal);
        Assert.Contains("/aiko-release git-release", body, StringComparison.Ordinal);
        Assert.Contains("travels in the text of the command", body, StringComparison.Ordinal);

        // Naming nothing is a question rather than a default, and an id that matches no scheme is a question
        // too: the procedure never chooses an order for the person.
        Assert.Contains("do not pick the first", body, StringComparison.Ordinal);
        Assert.Contains("ask which one to follow", body, StringComparison.Ordinal);
        Assert.Contains("is a question too", body, StringComparison.Ordinal);
        Assert.Contains("not a licence to choose for the person", body, StringComparison.Ordinal);

        // The record says which order the release took, so the id the procedure followed is the one it records.
        Assert.Contains("with the id", body, StringComparison.Ordinal);
        Assert.Contains("so the record says which order this release took", body, StringComparison.Ordinal);
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
