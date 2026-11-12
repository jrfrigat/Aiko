using Aiko.Application.Agents;
using Aiko.Domain.Execution;
using Aiko.Infrastructure.Agents;
using Xunit;

namespace Aiko.Infrastructure.Specs;

public class SdkAgentTransportSpecs
{
    [Fact]
    public async Task A_registered_adapter_runs_the_delivery()
    {
        var transport = new SdkAgentTransport(
            [new FakeAdapter("fake", new AgentTransportResult(true, SessionId: "sdk-1"))]);

        var result = await transport.DeliverAsync(Context("fake"), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("sdk-1", result.SessionId);
    }

    [Fact]
    public async Task A_failing_adapter_is_reported()
    {
        var transport = new SdkAgentTransport(
            [new FakeAdapter("fake", new AgentTransportResult(false, Message: "no"))]);

        var result = await transport.DeliverAsync(Context("fake"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("no", result.Message);
    }

    [Fact]
    public async Task An_adapter_exception_is_reported()
    {
        var transport = new SdkAgentTransport(
            [new FakeAdapter("fake", exception: new InvalidOperationException("boom"))]);

        var result = await transport.DeliverAsync(Context("fake"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("boom", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_adapter_is_refused()
    {
        var transport = new SdkAgentTransport([]);

        var result = await transport.DeliverAsync(Context(null), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("not configured", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_unknown_adapter_is_refused()
    {
        var transport = new SdkAgentTransport([]);

        var result = await transport.DeliverAsync(Context("missing"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("not registered", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static AgentDeliveryContext Context(string? adapterId) => new(
        "project-1",
        new CardCommand(
            "CMD-1",
            "TASK-1",
            CardCommandAction.Start,
            "implementation",
            null,
            "sdk-agent",
            null,
            CardCommandState.Queued,
            "you",
            DateTimeOffset.UtcNow,
            null,
            null,
            null),
        new AgentDelivery(
            "DLV-1",
            "CMD-1",
            "sdk-agent",
            AgentTransport.Sdk,
            AgentDeliveryState.Starting,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            null,
            null),
        new EffectiveAgentConfig(
            "sdk-agent",
            true,
            AgentTransport.Sdk,
            AgentTransport.Command,
            0,
            AgentCapabilityProfile.Default,
            null,
            adapterId is null ? null : new AgentSdkConfig(adapterId),
            true,
            [AgentTransport.Command, AgentTransport.Sdk],
            new Dictionary<string, AgentConfigSource>()));

    private sealed class FakeAdapter(
        string id,
        AgentTransportResult? result = null,
        Exception? exception = null) : IAgentSdkAdapter
    {
        public string Id => id;

        public ValueTask<AgentTransportResult> DeliverAsync(
            AgentDeliveryContext context,
            CancellationToken cancellationToken) =>
            exception is not null
                ? ValueTask.FromException<AgentTransportResult>(exception)
                : ValueTask.FromResult(result!);
    }
}
