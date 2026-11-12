using Aiko.Application.Agents;
using Aiko.Application.Contracts;
using Aiko.Domain.Execution;

namespace Aiko.Infrastructure.Agents;

/// <summary>
/// Sweeps the open commands of every project and hands each one either to a capable agent's transport or
/// leaves it in the queue. The queue stays the durable record; this service only tries to deliver it.
/// </summary>
public sealed class AgentDispatchService(
    IProjectCatalog projects,
    ICardCommandStore commands,
    IAgentDeliveryStore deliveries,
    IAgentRegistry registry,
    IEnumerable<IAgentTransport> transports,
    IAgentDispatchQueue queue)
{
    private readonly IReadOnlyList<IAgentTransport> transports = transports.ToArray();

    /// <summary>Marks every delivery left open by a previous daemon run as lost.</summary>
    public async Task RecoverAsync(CancellationToken cancellationToken)
    {
        var registered = await projects.ListAsync(cancellationToken);
        foreach (var project in registered)
        {
            var open = await deliveries.ListAsync(
                project.Id,
                includeClosed: false,
                cancellationToken);
            foreach (var delivery in open.Where(item => item.IsOpen))
            {
                await deliveries.UpdateAsync(
                    project.Id,
                    delivery with
                    {
                        State = AgentDeliveryState.Lost,
                        FailureReason = "Aiko restarted; the session cannot be resumed.",
                        FinishedAtUtc = DateTimeOffset.UtcNow
                    },
                    cancellationToken);
            }
        }
    }

    /// <summary>Runs one sweep. Exposed so a spec can drive it without the hosted loop.</summary>
    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        var registered = await projects.ListAsync(cancellationToken);
        foreach (var project in registered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await ProcessProjectAsync(project.Id, cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // One broken project must not stop the sweep for the others; its own error is visible in
                // the project's files and screens.
            }
        }
    }

    /// <summary>Runs the startup sweep and then wakes on every signal until the daemon stops.</summary>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RecoverAsync(stoppingToken);
            await ProcessAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await queue.WaitAsync(stoppingToken);
                await ProcessAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                // The next signal retries the sweep. A failed transport attempt is recorded on the delivery.
            }
        }
    }

    private async Task ProcessProjectAsync(string projectId, CancellationToken cancellationToken)
    {
        var open = await commands.ListAsync(projectId, includeClosed: false, cancellationToken);
        foreach (var command in open.Where(item => item.State is CardCommandState.Queued))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var latest = await deliveries.FindLatestForCommandAsync(
                projectId,
                command.Id,
                cancellationToken);
            if (latest is { IsOpen: true })
            {
                continue;
            }

            var route = await registry.ResolveAsync(
                projectId,
                new AgentCommandRequest(
                    command.Action,
                    CardId: command.CardId,
                    StageId: command.StageId,
                    RequestedAgentId: command.AgentAdapterId),
                cancellationToken);
            if (!route.IsDirect || route.Agent is null)
            {
                continue;
            }

            var transport = transports.FirstOrDefault(item => item.Transport == route.Transport);
            if (transport is null)
            {
                continue;
            }

            await DeliverAsync(projectId, command, route.Agent, transport, cancellationToken);
        }
    }

    private async Task DeliverAsync(
        string projectId,
        CardCommand command,
        EffectiveAgentConfig agent,
        IAgentTransport transport,
        CancellationToken cancellationToken)
    {
        var delivery = await deliveries.CreateAsync(
            projectId,
            command.Id,
            agent.Id,
            agent.AdapterId,
            transport.Transport,
            cancellationToken);

        CardCommand claimed;
        try
        {
            claimed = await commands.ClaimForDeliveryAsync(
                projectId,
                command.Id,
                agent.Id,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or FileNotFoundException)
        {
            await deliveries.UpdateAsync(
                projectId,
                delivery with
                {
                    State = AgentDeliveryState.Failed,
                    FailureReason = exception.Message,
                    FinishedAtUtc = DateTimeOffset.UtcNow
                },
                cancellationToken);
            return;
        }

        var running = delivery with
        {
            State = AgentDeliveryState.Running,
            StartedAtUtc = DateTimeOffset.UtcNow
        };
        await deliveries.UpdateAsync(projectId, running, cancellationToken);

        try
        {
            var result = await transport.DeliverAsync(
                new AgentDeliveryContext(projectId, claimed, running, agent),
                cancellationToken);
            await deliveries.UpdateAsync(
                projectId,
                running with
                {
                    State = result.Succeeded
                        ? AgentDeliveryState.Completed
                        : AgentDeliveryState.Failed,
                    SessionId = result.SessionId,
                    FailureReason = result.Succeeded ? null : result.Message,
                    FinishedAtUtc = DateTimeOffset.UtcNow
                },
                cancellationToken);

            if (!result.Succeeded)
            {
                await ReleaseAsync(projectId, command.Id, result.Message, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await deliveries.UpdateAsync(
                projectId,
                running with
                {
                    State = AgentDeliveryState.Interrupted,
                    FailureReason = "The daemon stopped during delivery.",
                    FinishedAtUtc = DateTimeOffset.UtcNow
                },
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            await deliveries.UpdateAsync(
                projectId,
                running with
                {
                    State = AgentDeliveryState.Failed,
                    FailureReason = exception.Message,
                    FinishedAtUtc = DateTimeOffset.UtcNow
                },
                cancellationToken);
            await ReleaseAsync(projectId, command.Id, exception.Message, cancellationToken);
        }
    }

    private async Task ReleaseAsync(
        string projectId,
        string commandId,
        string? reason,
        CancellationToken cancellationToken)
    {
        try
        {
            await commands.ReleaseAsync(projectId, commandId, reason, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // The agent closed the command while the transport was failing; there is nothing to release.
        }
    }
}
