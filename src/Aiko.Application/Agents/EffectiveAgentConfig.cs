using Aiko.Domain.Execution;

namespace Aiko.Application.Agents;

/// <summary>The configuration an agent actually runs with, after every layer is merged.</summary>
/// <param name="Id">Adapter identifier.</param>
/// <param name="Enabled">Whether the dispatcher may route work to the agent.</param>
/// <param name="Transport">Chosen transport.</param>
/// <param name="Fallback">Transport used when the chosen one is unavailable.</param>
/// <param name="Priority">Order among equally capable agents.</param>
/// <param name="Capabilities">Effective capabilities.</param>
/// <param name="Acp">Effective ACP settings, or null.</param>
/// <param name="Sdk">Effective SDK settings, or null.</param>
/// <param name="CommandClaim">Whether the agent may take commands from the queue.</param>
/// <param name="SupportedTransports">Transports the adapter supports at all.</param>
/// <param name="Sources">Which layer supplied each effective value.</param>
/// <param name="AdapterId">Adapter implementation the instance runs.</param>
/// <param name="DisplayName">Name the instance is shown with.</param>
public sealed record EffectiveAgentConfig(
    string Id,
    bool Enabled,
    AgentTransport Transport,
    AgentTransport Fallback,
    int Priority,
    AgentCapabilityProfile Capabilities,
    AgentAcpConfig? Acp,
    AgentSdkConfig? Sdk,
    bool CommandClaim,
    IReadOnlyList<AgentTransport> SupportedTransports,
    IReadOnlyDictionary<string, AgentConfigSource> Sources,
    string? AdapterId = null,
    string? DisplayName = null)
{
    /// <summary>True when a command can be handed to the agent without waiting for a claim.</summary>
    public bool CanDispatchDirectly =>
        Transport != AgentTransport.Command && SupportedTransports.Contains(Transport);
}
