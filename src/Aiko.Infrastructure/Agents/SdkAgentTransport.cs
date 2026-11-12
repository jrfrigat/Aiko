using Aiko.Application.Agents;
using Aiko.Domain.Execution;

namespace Aiko.Infrastructure.Agents;

/// <summary>
/// Dispatches a delivery to the SDK adapter named in the agent's configuration. No adapter ships yet:
/// Claude Agent SDK and dsh SDK are not .NET packages, and a CLI wrapped as one would not be an SDK.
/// </summary>
public sealed class SdkAgentTransport(IEnumerable<IAgentSdkAdapter> adapters) : IAgentTransport
{
    private readonly IReadOnlyDictionary<string, IAgentSdkAdapter> adaptersById = adapters
        .ToDictionary(adapter => adapter.Id, StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public AgentTransport Transport => AgentTransport.Sdk;

    /// <inheritdoc />
    public async ValueTask<AgentTransportResult> DeliverAsync(
        AgentDeliveryContext context,
        CancellationToken cancellationToken)
    {
        var id = context.Agent.Sdk?.Adapter;
        if (string.IsNullOrWhiteSpace(id))
        {
            return new AgentTransportResult(
                false,
                Message: "SDK adapter is not configured.");
        }

        if (!adaptersById.TryGetValue(id.Trim(), out var adapter))
        {
            return new AgentTransportResult(
                false,
                Message: $"SDK adapter '{id.Trim()}' is not registered.");
        }

        try
        {
            return await adapter.DeliverAsync(context, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new AgentTransportResult(false, Message: exception.Message);
        }
    }
}
