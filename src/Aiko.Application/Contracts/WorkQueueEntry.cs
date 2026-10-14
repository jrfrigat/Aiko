using Aiko.Domain.Workflow;

namespace Aiko.Application.Contracts;

/// <summary>One card of the project's work queue: what it is, where it sits, and why it may not be taken.</summary>
/// <remarks>
/// A view rather than a record of its own: every field is read from the card, the board's ranking, the start
/// gate's blockers and the latest run of the stage, so nothing here can disagree with the board. The scope
/// fields name the running runs whose declared scope overlaps this card's; <c>StageState</c> is
/// <c>Pending</c> for a stage that was never started.
/// </remarks>
public sealed record WorkQueueEntry(
    string CardId,
    string Title,
    string Kind,
    string WorkflowId,
    string StageId,
    string StageTitle,
    string? Size,
    decimal OwnPriority,
    decimal EffectivePriority,
    IReadOnlyList<CardBlocker> BlockedBy,
    string StageState,
    bool Finished,
    IReadOnlyList<string> ScopeConflictCardIds,
    bool ScopeOverlapBlocked);
