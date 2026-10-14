namespace Aiko.Application.Agents;

/// <summary>
/// Answer to a user-scope connect or disconnect: what the operation did and the adapter's state afterwards, so
/// one round trip cannot show a state older than the action just taken.
/// </summary>
public sealed record AgentConnectionResponse(
    AgentAdapterOption Adapter,
    AgentInstallationResult Result);
