using Aiko.Infrastructure.Agents;

namespace Aiko.Server;

/// <summary>Hosts the direct-delivery sweep in the daemon's lifetime.</summary>
internal sealed class AgentDispatchHostedService(AgentDispatchService dispatch) : BackgroundService
{
    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        dispatch.RunAsync(stoppingToken);
}
