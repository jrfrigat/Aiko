using System.Threading.Channels;
using Aiko.Application.Agents;

namespace Aiko.Infrastructure.Agents;

/// <summary>A one-slot signal that wakes the dispatcher without queueing a backlog of sweeps.</summary>
public sealed class AgentDispatchQueue : IAgentDispatchQueue
{
    private readonly Channel<bool> channel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });

    /// <inheritdoc />
    public void Signal() => channel.Writer.TryWrite(true);

    /// <inheritdoc />
    public async ValueTask WaitAsync(CancellationToken cancellationToken) =>
        await channel.Reader.ReadAsync(cancellationToken);
}
