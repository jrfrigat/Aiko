namespace Aiko.Domain.Cards;

/// <summary>
/// A card is the durable source of task or story state on the project board.
/// </summary>
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
    IReadOnlyDictionary<string, decimal>? CriterionValues = null);
