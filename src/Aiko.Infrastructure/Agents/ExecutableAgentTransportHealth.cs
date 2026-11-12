using Aiko.Application.Agents;
using Aiko.Domain.Execution;

namespace Aiko.Infrastructure.Agents;

/// <summary>
/// Availability of a transport without starting a session: command is always available, ACP needs its
/// launcher, and an SDK needs a configured adapter.
/// </summary>
public sealed class ExecutableAgentTransportHealth : IAgentTransportHealth
{
    /// <inheritdoc />
    public ValueTask<bool> IsAvailableAsync(
        EffectiveAgentConfig config,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var available = config.Transport switch
        {
            AgentTransport.Command => true,
            AgentTransport.Acp => config.Acp?.Executable is { Length: > 0 } executable &&
                (Path.IsPathRooted(executable)
                    ? File.Exists(executable)
                    : ExecutableDetector.Find([executable]).Count > 0),
            AgentTransport.Sdk => !string.IsNullOrWhiteSpace(config.Sdk?.Adapter),
            _ => false
        };
        return ValueTask.FromResult(available);
    }
}
