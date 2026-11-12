using Aiko.Domain.Execution;

namespace Aiko.Application.Agents;

/// <summary>Where a command is to be delivered.</summary>
/// <param name="Agent">Chosen agent, or null when any agent may take it from the queue.</param>
/// <param name="Transport">Transport the command is delivered by.</param>
/// <param name="IsDirect">True when the command is handed to the agent rather than left queued.</param>
/// <param name="Reason">Why the route is not direct, or null.</param>
/// <param name="Refusals">Agents that were considered and why they were not chosen.</param>
public sealed record AgentRoute(
    EffectiveAgentConfig? Agent,
    AgentTransport Transport,
    bool IsDirect,
    string? Reason,
    IReadOnlyList<AgentCandidateRefusal> Refusals)
{
    /// <summary>A route that leaves the command in the project queue.</summary>
    public static AgentRoute Queue(
        string? reason,
        IReadOnlyList<AgentCandidateRefusal>? refusals = null,
        EffectiveAgentConfig? agent = null) =>
        new(agent, AgentTransport.Command, false, reason, refusals ?? []);
}
