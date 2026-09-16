namespace Aiko.Domain.Execution;

/// <summary>
/// A single attempt of a specific agent to complete a stage. The full attempt history is preserved.
/// </summary>
public sealed record AgentAttempt(
    string Id,
    string AgentAdapterId,
    string? AgentInstallationId,
    AgentAttemptState State,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string? ExitReason);
