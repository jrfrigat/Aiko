using Aiko.Application.Agents;

namespace Aiko.Infrastructure.Agents;

/// <summary>The session counter used until direct sessions exist.</summary>
public sealed class ZeroAgentSessionCounter : IAgentSessionCounter
{
    /// <inheritdoc />
    public ValueTask<int> CountAsync(string instanceId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(0);
}
