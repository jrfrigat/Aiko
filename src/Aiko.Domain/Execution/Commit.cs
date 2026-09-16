using System.Text.Json.Serialization;

namespace Aiko.Domain.Execution;

/// <summary>
/// State of a Git commit reported for a stage execution.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CommitState>))]
public enum CommitState
{
    /// <summary>The agent asked to commit and is waiting for user approval.</summary>
    PendingApproval,

    /// <summary>The commit was recorded (either allowed directly or approved by the user).</summary>
    Recorded,

    /// <summary>The user rejected the commit request.</summary>
    Rejected
}

/// <summary>
/// A Git commit reported by an agent for a stage execution. The SHA is null while the commit
/// is still awaiting approval.
/// </summary>
public sealed record Commit(
    string? Sha,
    string Message,
    IReadOnlyList<string> Files,
    CommitState State,
    DateTimeOffset RecordedAtUtc);