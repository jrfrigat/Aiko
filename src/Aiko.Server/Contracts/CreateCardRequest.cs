namespace Aiko.Server.Contracts;

/// <summary>
/// Request to create a card through the REST API.
/// </summary>
/// <param name="CardId">
/// The card id, or null to let Aiko invent one. The id names the card's folder and is Aiko's own
/// bookkeeping, so a caller only names one to import a card that already has an id.
/// </param>
/// <param name="Kind">Card type id: <c>Story</c>, <c>Task</c>, or any type the project defines.</param>
/// <param name="Title">Human-readable title.</param>
/// <param name="WorkflowId">Workflow the card moves through, or null to take the type's own workflow.</param>
/// <param name="StageId">
/// Must be the backlog stage (or null/empty, which also means the backlog). A card is created unelaborated
/// and moves from there; naming another stage is rejected.
/// </param>
/// <param name="OwnPriority">Own priority before the size coefficient.</param>
/// <param name="DeclaredScopeFiles">File patterns the card intends to touch.</param>
/// <param name="CriterionValues">Per-criterion values, when the project scores by criteria.</param>
/// <param name="Size">Size step of the project's grid (ТЗ §10), for example <c>M</c>.</param>
/// <param name="Requirements">What the card is asked to do, stored in its metadata; null for none.</param>
/// <param name="Request">
/// What the user asked for, in their own words, stored in the card's metadata; null for none. It is the raw
/// wording rather than the reworked task, and the card stops being able to change it once it leaves the backlog.
/// </param>
public sealed record CreateCardRequest(
    string? CardId,
    string Kind,
    string Title,
    string? WorkflowId,
    string? StageId,
    decimal OwnPriority,
    IReadOnlyList<string>? DeclaredScopeFiles,
    IReadOnlyDictionary<string, decimal>? CriterionValues,
    string? Size = null,
    string? Requirements = null,
    string? Request = null);