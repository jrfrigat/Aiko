namespace Aiko.Server.Contracts;

/// <summary>
/// Request to ask an agent to estimate a card: to fill in the size step and the criterion values the
/// person left for the machine.
/// </summary>
/// <param name="AgentAdapterId">
/// Adapter id of the agent that should estimate the card, for example <c>claude-code</c>. The daemon cannot
/// run an agent (that is a post-MVP feature), so it records who was asked; the person runs the
/// <c>/aiko-estimate</c> command in that agent's terminal.
/// </param>
internal sealed record EstimateCardRequest(string AgentAdapterId);
