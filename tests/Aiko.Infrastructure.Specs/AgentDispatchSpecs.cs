using Aiko.Application.Agents;
using Aiko.Application.Contracts;
using Aiko.Domain.Execution;
using Aiko.Infrastructure.Agents;
using Xunit;

namespace Aiko.Infrastructure.Specs;

public class AgentDispatchSpecs
{
    [Fact]
    public async Task A_direct_delivery_claims_the_command_and_completes()
    {
        var command = QueuedCommand();
        var commands = new FakeCommandStore(command);
        var deliveries = new FakeDeliveryStore();
        var transport = new FakeTransport(
            new AgentTransportResult(true, SessionId: "session-1"));
        var service = Create(commands, deliveries, DirectRoute(), transport);

        await service.ProcessAsync(CancellationToken.None);

        Assert.Equal(CardCommandState.Taken, commands.Command.State);
        Assert.Null(commands.Command.AgentAdapterId);
        Assert.Single(deliveries.Entries);
        Assert.Equal(AgentDeliveryState.Completed, deliveries.Entries[0].State);
        Assert.Equal("session-1", deliveries.Entries[0].SessionId);
        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task A_failed_transport_returns_the_command_to_the_queue()
    {
        var command = QueuedCommand(requestedAgent: "alpha");
        var commands = new FakeCommandStore(command);
        var deliveries = new FakeDeliveryStore();
        var transport = new FakeTransport(
            new AgentTransportResult(false, Message: "no session"));
        var service = Create(commands, deliveries, DirectRoute(), transport);

        await service.ProcessAsync(CancellationToken.None);

        Assert.Equal(CardCommandState.Queued, commands.Command.State);
        Assert.Equal("alpha", commands.Command.AgentAdapterId);
        Assert.Single(deliveries.Entries);
        Assert.Equal(AgentDeliveryState.Failed, deliveries.Entries[0].State);
        Assert.Contains("no session", deliveries.Entries[0].FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_transport_exception_returns_the_command_to_the_queue()
    {
        var command = QueuedCommand();
        var commands = new FakeCommandStore(command);
        var deliveries = new FakeDeliveryStore();
        var transport = new FakeTransport(new InvalidOperationException("boom"));
        var service = Create(commands, deliveries, DirectRoute(), transport);

        await service.ProcessAsync(CancellationToken.None);

        Assert.Equal(CardCommandState.Queued, commands.Command.State);
        Assert.Equal(AgentDeliveryState.Failed, deliveries.Entries[0].State);
        Assert.Contains("boom", deliveries.Entries[0].FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_queue_only_route_leaves_the_command_alone()
    {
        var command = QueuedCommand();
        var commands = new FakeCommandStore(command);
        var deliveries = new FakeDeliveryStore();
        var transport = new FakeTransport(new AgentTransportResult(true));
        var service = Create(
            commands,
            deliveries,
            AgentRoute.Queue("no direct agent"),
            transport);

        await service.ProcessAsync(CancellationToken.None);

        Assert.Equal(CardCommandState.Queued, commands.Command.State);
        Assert.Empty(deliveries.Entries);
        Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public async Task The_agent_the_person_named_is_not_rewritten()
    {
        var command = QueuedCommand(requestedAgent: "alpha");
        var commands = new FakeCommandStore(command);
        var deliveries = new FakeDeliveryStore();
        var transport = new FakeTransport(new AgentTransportResult(true, SessionId: "s"));
        var service = Create(commands, deliveries, DirectRoute(), transport);

        await service.ProcessAsync(CancellationToken.None);

        Assert.Equal(CardCommandState.Taken, commands.Command.State);
        Assert.Equal("alpha", commands.Command.AgentAdapterId);
        Assert.Equal("alpha", deliveries.Entries[0].AgentAdapterId);
    }

    private static CardCommand QueuedCommand(string? requestedAgent = null) => new(
        "CMD-1",
        "TASK-1",
        CardCommandAction.Start,
        "implementation",
        null,
        requestedAgent,
        null,
        CardCommandState.Queued,
        "you",
        DateTimeOffset.UtcNow,
        null,
        null,
        null);

    private static AgentRoute DirectRoute() => new(
        Agent(),
        AgentTransport.Acp,
        true,
        null,
        []);

    private static EffectiveAgentConfig Agent() => new(
        "alpha",
        true,
        AgentTransport.Acp,
        AgentTransport.Command,
        0,
        AgentCapabilityProfile.Default,
        new AgentAcpConfig(Executable: "dotnet"),
        null,
        true,
        [AgentTransport.Command, AgentTransport.Acp],
        new Dictionary<string, AgentConfigSource>());

    private static AgentDispatchService Create(
        ICardCommandStore commands,
        IAgentDeliveryStore deliveries,
        AgentRoute route,
        FakeTransport transport) => new(
        new FakeProjects(),
        commands,
        deliveries,
        new FakeRegistry(route),
        [transport],
        new FakeQueue());

    private sealed class FakeProjects : IProjectCatalog
    {
        private static readonly RegisteredProject Project =
            new("project-1", "Project", Path.GetTempPath(), "project");

        public ValueTask<IReadOnlyList<RegisteredProject>> ListAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<RegisteredProject>>([Project]);

        public ValueTask<RegisteredProject?> FindAsync(
            string projectIdOrSlug,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<RegisteredProject?>(Project);

        public ValueTask<bool> IsSlugTakenAsync(
            string slug,
            string? exceptProjectId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(false);

        public ValueTask SaveAsync(
            RegisteredProject project,
            CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask<bool> RemoveAsync(
            string projectId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(false);
    }

    private sealed class FakeRegistry(AgentRoute route) : IAgentRegistry
    {
        public ValueTask<IReadOnlyList<RegisteredAgent>> ListAsync(
            string? projectId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<RegisteredAgent>>([]);

        public ValueTask<AgentRoute> ResolveAsync(
            string? projectId,
            AgentCommandRequest request,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(route);
    }

    private sealed class FakeQueue : IAgentDispatchQueue
    {
        public void Signal()
        {
        }

        public ValueTask WaitAsync(CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class FakeTransport : IAgentTransport
    {
        private readonly AgentTransportResult? result;
        private readonly Exception? exception;

        public FakeTransport(AgentTransportResult result) => this.result = result;

        public FakeTransport(Exception exception) => this.exception = exception;

        public int Calls { get; private set; }

        public AgentTransport Transport => AgentTransport.Acp;

        public ValueTask<AgentTransportResult> DeliverAsync(
            AgentDeliveryContext context,
            CancellationToken cancellationToken)
        {
            Calls++;
            return exception is not null
                ? ValueTask.FromException<AgentTransportResult>(exception)
                : ValueTask.FromResult(result!);
        }
    }

    private sealed class FakeCommandStore(CardCommand command) : ICardCommandStore
    {
        public CardCommand Command { get; private set; } = command;

        public ValueTask<IReadOnlyList<CardCommand>> ListAsync(
            string projectId,
            bool includeClosed,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<CardCommand>>(
                Command.IsOpen ? [Command] : []);

        public ValueTask<CardCommand?> FindAsync(
            string projectId,
            string commandId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<CardCommand?>(Command);

        public ValueTask<CardCommand> PlaceAsync(
            string projectId,
            PlaceCommandRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<CardCommand> ClaimAsync(
            string projectId,
            string commandId,
            string agentAdapterId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<CardCommand> ClaimForDeliveryAsync(
            string projectId,
            string commandId,
            string agentAdapterId,
            CancellationToken cancellationToken)
        {
            Command = Command with
            {
                State = CardCommandState.Taken,
                ClaimedAtUtc = DateTimeOffset.UtcNow
            };
            return ValueTask.FromResult(Command);
        }

        public ValueTask<CardCommand> ReleaseAsync(
            string projectId,
            string commandId,
            string? reason,
            CancellationToken cancellationToken)
        {
            Command = Command with
            {
                State = CardCommandState.Queued,
                ClaimedAtUtc = null,
                Message = reason
            };
            return ValueTask.FromResult(Command);
        }

        public ValueTask<CardCommand> FinishAsync(
            string projectId,
            string commandId,
            CardCommandState state,
            string? message,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<CardCommand> CancelAsync(
            string projectId,
            string commandId,
            string? reason,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeDeliveryStore : IAgentDeliveryStore
    {
        private int sequence;

        public List<AgentDelivery> Entries { get; } = [];

        public ValueTask<IReadOnlyList<AgentDelivery>> ListAsync(
            string projectId,
            bool includeClosed,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<AgentDelivery>>(
                includeClosed ? Entries : [.. Entries.Where(entry => entry.IsOpen)]);

        public ValueTask<AgentDelivery?> FindAsync(
            string projectId,
            string deliveryId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<AgentDelivery?>(
                Entries.FirstOrDefault(entry => entry.Id == deliveryId));

        public ValueTask<AgentDelivery?> FindLatestForCommandAsync(
            string projectId,
            string commandId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<AgentDelivery?>(
                Entries.LastOrDefault(entry => entry.CommandId == commandId));

        public ValueTask<AgentDelivery> CreateAsync(
            string projectId,
            string commandId,
            string agentAdapterId,
            AgentTransport transport,
            CancellationToken cancellationToken)
        {
            var delivery = new AgentDelivery(
                $"DLV-{++sequence}",
                commandId,
                agentAdapterId,
                transport,
                AgentDeliveryState.Starting,
                null,
                null,
                null,
                DateTimeOffset.UtcNow,
                null,
                null);
            Entries.Add(delivery);
            return ValueTask.FromResult(delivery);
        }

        public ValueTask<AgentDelivery> UpdateAsync(
            string projectId,
            AgentDelivery delivery,
            CancellationToken cancellationToken)
        {
            var index = Entries.FindIndex(entry => entry.Id == delivery.Id);
            Entries[index] = delivery;
            return ValueTask.FromResult(delivery);
        }
    }
}
