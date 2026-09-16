namespace Aiko.Application.Agents;

/// <summary>
/// A discovered agent installation: a concrete executable of the adapter's agent.
/// </summary>
public sealed record AgentInstallation(string Id, string AdapterId, string ExecutablePath, string? Version);
