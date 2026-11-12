namespace Aiko.Application.Agents;

/// <summary>Why one agent was not chosen for a command.</summary>
/// <param name="AgentAdapterId">Agent that was considered.</param>
/// <param name="Reason">What it lacks or why it is unavailable.</param>
public sealed record AgentCandidateRefusal(string AgentAdapterId, string Reason);
