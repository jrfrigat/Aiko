using Aiko.Domain.Cards;

namespace Aiko.Server.Contracts;

/// <summary>
/// Request to create a card through the REST API.
/// </summary>
/// <param name="CardId">Stable file-safe card id.</param>
/// <param name="Kind">Story or task.</param>
/// <param name="Title">Human-readable title.</param>
/// <param name="WorkflowId">Workflow the card moves through.</param>
/// <param name="StageId">Initial stage.</param>
/// <param name="OwnPriority">Own priority before the size coefficient.</param>
/// <param name="DeclaredScopeFiles">File patterns the card intends to touch.</param>
/// <param name="CriterionValues">Per-criterion values, when the project scores by criteria.</param>
/// <param name="Size">Size step of the project's grid (ТЗ §10), for example <c>M</c>.</param>
public sealed record CreateCardRequest(
    string CardId,
    CardKind Kind,
    string Title,
    string WorkflowId,
    string StageId,
    decimal OwnPriority,
    IReadOnlyList<string>? DeclaredScopeFiles,
    IReadOnlyDictionary<string, decimal>? CriterionValues,
    string? Size = null);