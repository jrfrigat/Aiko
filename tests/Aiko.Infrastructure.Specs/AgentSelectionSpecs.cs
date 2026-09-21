using Aiko.Application.Agents;
using Aiko.Domain.Execution;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// Which agents a user-scope installation targets. The rule is checked with adapters this spec owns rather
/// than by running <c>aiko agent install --scope user</c>: that command writes the global MCP entry and the
/// <c>/aiko-*</c> skills into the user's own agent configuration, so a spec that exercised it would edit
/// somebody's home directory - and pointing PATH at a directory of its own would race the specs that already
/// redirect PATH. Whether an adapter is installed is a property of the adapter here, which is exactly what
/// the rule reads.
/// </summary>
public sealed class AgentSelectionSpecs
{
    [Fact]
    public async Task An_explicit_agent_option_wins_over_discovery()
    {
        // codex is installed and claude-code is not: naming claude-code takes it as given, and the detected
        // agent is not added behind the caller's back.
        var adapters = new[] { Installed("codex"), Missing("claude-code") };

        var targets = await AgentSelection.ResolveAsync(adapters, "claude-code", CancellationToken.None);

        Assert.Equal(["claude-code"], targets);
    }

    [Fact]
    public async Task Without_the_agent_option_only_the_installed_adapters_are_chosen()
    {
        var adapters = new[] { Installed("claude-code"), Missing("codex"), Missing("cursor") };

        var targets = await AgentSelection.ResolveAsync(adapters, null, CancellationToken.None);

        Assert.Equal(["claude-code"], targets);
    }

    [Fact]
    public async Task A_machine_with_no_agent_chooses_nobody_rather_than_everybody()
    {
        var adapters = new[]
        {
            Missing("claude-code"),
            Missing("codex"),
            Missing("cursor"),
            Missing("zcode"),
            Missing("cline")
        };

        var targets = await AgentSelection.ResolveAsync(adapters, null, CancellationToken.None);

        // The edge that matters: an empty choice says "nothing to connect". Falling back to every adapter here
        // is what wrote a configuration into agents that are not on the machine.
        Assert.Empty(targets);
    }

    [Fact]
    public async Task A_blank_agent_option_asks_discovery_instead_of_naming_anybody()
    {
        var adapters = new[] { Installed("claude-code"), Missing("codex") };

        // `--agent ""` and `--agent ,` carry no identifier, so neither may be read as "everybody".
        Assert.Equal(
            ["claude-code"],
            await AgentSelection.ResolveAsync(adapters, string.Empty, CancellationToken.None));
        Assert.Equal(
            ["claude-code"],
            await AgentSelection.ResolveAsync(adapters, " , ", CancellationToken.None));
    }

    [Fact]
    public void The_agent_option_is_a_comma_separated_list_with_blanks_dropped()
    {
        Assert.Empty(AgentSelection.Parse(null));
        Assert.Empty(AgentSelection.Parse("  "));
        Assert.Equal(["codex", "zcode"], AgentSelection.Parse("codex, ,zcode,"));
    }

    private static StubAdapter Installed(string id) =>
        new(id, [new AgentInstallation($"{id}:found", id, $@"C:\tools\{id}.exe", null)]);

    private static StubAdapter Missing(string id) => new(id, []);

    /// <summary>
    /// An adapter that answers only discovery, which is all the selection asks of one. Every other member is
    /// outside this spec's subject, and reaching one is a mistake rather than a result.
    /// </summary>
    private sealed class StubAdapter(string id, IReadOnlyList<AgentInstallation> installations) : IAgentAdapter
    {
        public string Id => id;

        public string DisplayName => id;

        public AgentCapabilities Capabilities => AgentCapabilities.McpStdio;

        public ValueTask<IReadOnlyList<AgentInstallation>> DetectInstallationsAsync(
            CancellationToken cancellationToken) => ValueTask.FromResult(installations);

        public ValueTask<InstallationPlan> PlanProjectInstallAsync(
            string projectRoot,
            string projectHandle,
            string projectMcpEndpoint,
            string? accessToken,
            IReadOnlyList<CardTypeDescriptor> cardTypes,
            CancellationToken cancellationToken) => throw Unexpected();

        public ValueTask<AgentInstallationResult> ApplyProjectInstallAsync(
            string projectRoot,
            string projectHandle,
            string projectMcpEndpoint,
            string? accessToken,
            IReadOnlyList<CardTypeDescriptor> cardTypes,
            CancellationToken cancellationToken) => throw Unexpected();

        public ValueTask<bool> IsProjectConfiguredAsync(
            string projectRoot,
            CancellationToken cancellationToken) => throw Unexpected();

        public ValueTask<InstallationPlan> PlanProjectUninstallAsync(
            string projectRoot,
            string projectHandle,
            CancellationToken cancellationToken) => throw Unexpected();

        public ValueTask<AgentInstallationResult> UninstallProjectAsync(
            string projectRoot,
            string projectHandle,
            CancellationToken cancellationToken) => throw Unexpected();

        public ValueTask<InstallationPlan> PlanUserInstallAsync(CancellationToken cancellationToken) =>
            throw Unexpected();

        public ValueTask<AgentInstallationResult> ApplyUserInstallAsync(CancellationToken cancellationToken) =>
            throw Unexpected();

        public ValueTask<InstallationPlan> PlanUserUninstallAsync(CancellationToken cancellationToken) =>
            throw Unexpected();

        public ValueTask<AgentInstallationResult> UninstallUserAsync(CancellationToken cancellationToken) =>
            throw Unexpected();

        public ValueTask<AgentAttemptState> ClassifyExitAsync(
            int exitCode,
            string standardError,
            CancellationToken cancellationToken) => throw Unexpected();

        private static NotSupportedException Unexpected() =>
            new("The selection only asks an adapter what it has found; planning and applying belong to a real adapter.");
    }
}
