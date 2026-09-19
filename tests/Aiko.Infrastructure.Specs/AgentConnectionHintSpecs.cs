using Aiko.Application.Agents;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// The hint a person acts on when an agent was installed after Aiko: it has to name the agent and the one
/// command that connects it, and it has to stay silent about agents that need nothing.
/// </summary>
public sealed class AgentConnectionHintSpecs
{
    [Fact]
    public void An_agent_on_path_without_the_configuration_needs_connecting()
    {
        var option = Option(Installations(), new AgentUserScope(0, 9));

        Assert.True(AgentConnectionHint.NeedsConnection(option));
        var hint = AgentConnectionHint.Describe(option);
        Assert.NotNull(hint);
        Assert.Contains("codex", hint);
        Assert.Contains("not connected", hint);
        Assert.Contains("aiko agent install --agent codex --scope user", hint);
    }

    [Fact]
    public void A_partly_written_configuration_says_so()
    {
        var hint = AgentConnectionHint.Describe(Option(Installations(), new AgentUserScope(3, 9)));

        Assert.NotNull(hint);
        Assert.Contains("partially connected", hint);
    }

    [Fact]
    public void A_connected_agent_and_a_missing_one_stay_silent()
    {
        // Everything Aiko writes is present: nothing to offer.
        Assert.Null(AgentConnectionHint.Describe(Option(Installations(), new AgentUserScope(9, 9))));

        // Not on PATH at all: there is nothing to connect.
        Assert.Null(AgentConnectionHint.Describe(Option([])));
    }

    [Fact]
    public void An_adapter_without_a_user_scope_can_be_detected_but_never_connected()
    {
        var option = Option(Installations(), new AgentUserScope(0, 0));

        Assert.True(AgentConnectionHint.NeedsConnection(option));
        Assert.Contains("not connected", AgentConnectionHint.Describe(option));
    }

    private static AgentAdapterOption Option(
        IReadOnlyList<AgentInstallation> installations,
        AgentUserScope? userScope = null) =>
        new("codex", "Codex", AgentCapabilities.McpStdio, installations, false, userScope);

    private static IReadOnlyList<AgentInstallation> Installations() =>
        [new AgentInstallation("codex:/usr/bin/codex", "codex", "/usr/bin/codex", null)];
}
