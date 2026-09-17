namespace Aiko.Domain.Cards;

/// <summary>
/// A card is the durable source of task or story state on the project board.
/// </summary>
/// <param name="Reference">Project-qualified identifier.</param>
/// <param name="Kind">Story or task.</param>
/// <param name="Title">Human-readable title.</param>
/// <param name="WorkflowId">Workflow the card moves through.</param>
/// <param name="StageId">Current stage.</param>
/// <param name="Revision">Optimistic concurrency revision.</param>
/// <param name="OwnPriority">The card's own priority, before parents and before the size coefficient.</param>
/// <param name="DeclaredScopeFiles">File patterns the card intends to touch.</param>
/// <param name="ActualChangedFiles">Files the agents actually changed.</param>
/// <param name="Metadata">Free-form card metadata.</param>
/// <param name="Origin">Where a cross-project card came from.</param>
/// <param name="CriterionValues">Per-criterion values keyed by criterion id, when the project scores by criteria.</param>
/// <param name="Size">
/// The size step of the project's grid (ТЗ §10), for example <c>M</c>. Null on cards written before the
/// grid existed or never sized; an unsized card is neutral in the formula. The agent assigns it from the
/// grid's descriptions, which is why the grid carries them.
/// </param>
public sealed record Card(
    CardReference Reference,
    CardKind Kind,
    string Title,
    string WorkflowId,
    string StageId,
    long Revision,
    decimal OwnPriority,
    IReadOnlyList<string> DeclaredScopeFiles,
    IReadOnlyList<string> ActualChangedFiles,
    IReadOnlyDictionary<string, string> Metadata,
    CardOrigin? Origin = null,
    IReadOnlyDictionary<string, decimal>? CriterionValues = null,
    string? Size = null);
