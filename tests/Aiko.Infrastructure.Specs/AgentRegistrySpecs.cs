using Aiko.Application.Agents;
using Aiko.Domain.Execution;
using Aiko.Infrastructure.Agents;
using Xunit;

namespace Aiko.Infrastructure.Specs;

public class AgentRegistrySpecs
{
    [Fact]
    public async Task A_capable_acp_agent_is_chosen_directly()
    {
        var registry = Create(AcpAgent("alpha"));

        var route = await registry.ResolveAsync(null, Start(), CancellationToken.None);

        Assert.True(route.IsDirect);
        Assert.Equal(AgentTransport.Acp, route.Transport);
        Assert.Equal("alpha", route.Agent!.Id);
        Assert.Empty(route.Refusals);
    }

    [Fact]
    public async Task An_explicitly_named_agent_is_not_replaced()
    {
        var registry = Create(AcpAgent("alpha"), CommandAgent("beta"));

        var route = await registry.ResolveAsync(
            null,
            Start(requested: "beta"),
            CancellationToken.None);

        Assert.False(route.IsDirect);
        Assert.Equal("beta", route.Agent!.Id);
        Assert.Equal(AgentTransport.Command, route.Transport);
        Assert.Contains("queue", route.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(route.Refusals, refusal => refusal.AgentAdapterId == "beta");
    }

    [Fact]
    public async Task An_action_outside_the_capabilities_is_refused()
    {
        var alpha = AcpAgent(
            "alpha",
            capabilities: AgentCapabilityProfile.Default with { Actions = ["start"] });
        var registry = Create(alpha);

        var route = await registry.ResolveAsync(
            null,
            new AgentCommandRequest(CardCommandAction.Pause, CardId: "TASK-1"),
            CancellationToken.None);

        Assert.False(route.IsDirect);
        Assert.Contains(
            route.Refusals,
            refusal => refusal.AgentAdapterId == "alpha" &&
                refusal.Reason.Contains("pause", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_busy_agent_is_skipped()
    {
        var registry = Create(
            [AcpAgent("alpha")],
            _ => true,
            _ => 1);

        var route = await registry.ResolveAsync(null, Start(), CancellationToken.None);

        Assert.False(route.IsDirect);
        Assert.Contains(
            route.Refusals,
            refusal => refusal.AgentAdapterId == "alpha" &&
                refusal.Reason.Contains("session slot", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Priority_orders_direct_candidates()
    {
        var registry = Create(
            AcpAgent("alpha", priority: 1),
            AcpAgent("gamma", priority: 10));

        var route = await registry.ResolveAsync(null, Start(), CancellationToken.None);

        Assert.True(route.IsDirect);
        Assert.Equal("gamma", route.Agent!.Id);
    }

    [Fact]
    public async Task An_unavailable_transport_falls_back_to_the_queue()
    {
        var registry = Create(
            [AcpAgent("alpha")],
            config => !string.Equals(config.Id, "alpha", StringComparison.Ordinal),
            _ => 0);

        var route = await registry.ResolveAsync(null, Start(), CancellationToken.None);

        Assert.False(route.IsDirect);
        Assert.Contains(
            route.Refusals,
            refusal => refusal.AgentAdapterId == "alpha" &&
                refusal.Reason.Contains("not available", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task An_unknown_explicit_agent_is_refused()
    {
        var registry = Create(AcpAgent("alpha"));

        var route = await registry.ResolveAsync(
            null,
            Start(requested: "missing"),
            CancellationToken.None);

        Assert.False(route.IsDirect);
        Assert.Contains("Unknown agent", route.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_preferred_agent_that_cannot_go_direct_does_not_block_another()
    {
        var registry = Create(AcpAgent("alpha"), CommandAgent("beta"));

        var route = await registry.ResolveAsync(
            null,
            Start(defaultAgent: "beta"),
            CancellationToken.None);

        Assert.True(route.IsDirect);
        Assert.Equal("alpha", route.Agent!.Id);
        Assert.Contains(route.Refusals, refusal => refusal.AgentAdapterId == "beta");
    }

    private static AgentCommandRequest Start(
        string? requested = null,
        string? defaultAgent = null) => new(
        CardCommandAction.Start,
        CardId: "TASK-1",
        StageId: "implementation",
        CardTypeId: "task",
        RequestedAgentAdapterId: requested,
        DefaultAgentAdapterId: defaultAgent);

    private static StubAdapter AcpAgent(
        string id,
        int priority = 0,
        AgentCapabilityProfile? capabilities = null) =>
        new(
            id,
            AgentCapabilities.CommandQueue | AgentCapabilities.AcpTransport | AgentCapabilities.AikoMcp,
            AgentConfig.Default(id) with
            {
                Transport = AgentTransport.Acp,
                Priority = priority,
                Capabilities = capabilities ?? AgentCapabilityProfile.Default,
                Acp = new AgentAcpConfig(Executable: "dotnet", Args: ["--version"])
            });

    private static StubAdapter CommandAgent(string id) =>
        new(
            id,
            AgentCapabilities.CommandQueue | AgentCapabilities.AikoMcp,
            AgentConfig.Default(id));

    private static AgentRegistry Create(params IAgentAdapter[] adapters) =>
        Create(adapters, _ => true, _ => 0);

    private static AgentRegistry Create(
        IAgentAdapter[] adapters,
        Func<EffectiveAgentConfig, bool> health,
        Func<string, int> sessions) =>
        new(
            adapters,
            new AgentConfigService(adapters, new EmptyStore()),
            new StubHealth(health),
            new StubSessions(sessions));

    private sealed class StubHealth(Func<EffectiveAgentConfig, bool> isAvailable) : IAgentTransportHealth
    {
        public ValueTask<bool> IsAvailableAsync(
            EffectiveAgentConfig config,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(isAvailable(config));
    }

    private sealed class StubSessions(Func<string, int> count) : IAgentSessionCounter
    {
        public ValueTask<int> CountAsync(
            string adapterId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(count(adapterId));
    }

    private sealed class EmptyStore : IAgentConfigStore
    {
        public ValueTask<AgentConfig?> ReadUserAsync(
            string adapterId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<AgentConfig?>(null);

        public ValueTask<AgentConfig?> ReadProjectAsync(
            string projectId,
            string adapterId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<AgentConfig?>(null);

        public ValueTask SaveUserAsync(AgentConfig config, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask SaveProjectAsync(
            string projectId,
            AgentConfig config,
            CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask<IReadOnlyList<string>> ListUserAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<string>>([]);

        public ValueTask<IReadOnlyList<string>> ListProjectAsync(
            string projectId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class StubAdapter(
        string id,
        AgentCapabilities capabilities,
        AgentConfig defaults) : IAgentAdapter
    {
        public string Id => id;

        public string DisplayName => id;

        public AgentCapabilities Capabilities => capabilities;

        public AgentConfig DefaultConfig => defaults;

        public ValueTask<IReadOnlyList<AgentInstallation>> DetectInstallationsAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<AgentInstallation>>(
                [new AgentInstallation($"{id}:found", id, $@"C:\tools\{id}.exe", null)]);

        public ValueTask<InstallationPlan> PlanProjectInstallAsync(
            string projectRoot,
            string projectHandle,
            string projectMcpEndpoint,
            string? accessToken,
            IReadOnlyList<CardTypeDescriptor> cardTypes,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<AgentInstallationResult> ApplyProjectInstallAsync(
            string projectRoot,
            string projectHandle,
            string projectMcpEndpoint,
            string? accessToken,
            IReadOnlyList<CardTypeDescriptor> cardTypes,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<bool> IsProjectConfiguredAsync(
            string projectRoot,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<bool> IsUserConfiguredAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<InstallationPlan> PlanProjectUninstallAsync(
            string projectRoot,
            string projectHandle,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<AgentInstallationResult> UninstallProjectAsync(
            string projectRoot,
            string projectHandle,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<InstallationPlan> PlanUserInstallAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<AgentInstallationResult> ApplyUserInstallAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<InstallationPlan> PlanUserUninstallAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<AgentInstallationResult> UninstallUserAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<AgentAttemptState> ClassifyExitAsync(
            int exitCode,
            string standardError,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
