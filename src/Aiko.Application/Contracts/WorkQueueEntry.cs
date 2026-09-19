using Aiko.Domain.Workflow;

namespace Aiko.Application.Contracts;

/// <summary>
/// One card of the project's work queue: what it is, where it sits, and why it may not be taken yet.
/// </summary>
/// <remarks>
/// A view rather than a record of its own: every field is read from something that already exists - the card,
/// the priority the board computes, the blockers the start gate enforces, the latest run of the card's stage.
/// Nothing here is stored, so nothing here can disagree with the board beside it.
/// </remarks>
/// <param name="CardId">Card the entry is about.</param>
/// <param name="Title">Its title, so a queue reads without a second call.</param>
/// <param name="Kind">Card type id.</param>
/// <param name="WorkflowId">Pipeline the card's type defines.</param>
/// <param name="StageId">Stage the card sits in now.</param>
/// <param name="StageTitle">That stage's title.</param>
/// <param name="Size">Size step of the project's grid, or null when the card has none.</param>
/// <param name="OwnPriority">The card's own score before the formula.</param>
/// <param name="EffectivePriority">The score the board ranks by, computed by the same projector the board uses.</param>
/// <param name="BlockedBy">
/// The cards that block this one and are not finished yet, from the rule the start gate applies. Empty when
/// the card is free to be worked.
/// </param>
/// <param name="StageState">
/// State of the latest run of the card's current stage, as <c>StageRunSummary</c> writes it, or
/// <c>Pending</c> when the stage was never started.
/// </param>
/// <param name="Finished">
/// True when the card reached the last stage of its own pipeline and that stage has a finished run - the
/// point past which there is nothing left to work.
/// </param>
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
    bool Finished);
