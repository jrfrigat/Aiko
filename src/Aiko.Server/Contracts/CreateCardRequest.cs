using Aiko.Domain.Cards;

namespace Aiko.Server.Contracts;

/// <summary>
/// Request to create a card through the REST API.
/// </summary>
public sealed record CreateCardRequest(
    string CardId,
    CardKind Kind,
    string Title,
    string WorkflowId,
    string StageId,
    decimal OwnPriority,
    IReadOnlyList<string>? DeclaredScopeFiles,
    IReadOnlyDictionary<string, decimal>? CriterionValues);