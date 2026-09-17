using Aiko.Domain.Cards;
using Aiko.Domain.Workflow;

namespace Aiko.Pwa.Contracts;

/// <summary>
/// Request shapes of the PWA. They are top-level types on purpose: the client serializes JSON through
/// <see cref="Services.PwaJsonContext"/>, and a source-generated serializer needs the types to be
/// reachable from the generated code (a private type nested in a component is not).
/// </summary>
internal sealed record CreateCardRequest(
    string CardId,
    CardKind Kind,
    string Title,
    string WorkflowId,
    string StageId,
    decimal OwnPriority,
    IReadOnlyList<string>? DeclaredScopeFiles,
    IReadOnlyDictionary<string, decimal>? CriterionValues,
    string? Size = null);

/// <summary>
/// Moves a card to another workflow stage.
/// </summary>
internal sealed record MoveCardRequest(
    string StageId,
    long ExpectedRevision);

/// <summary>
/// Updates the editable fields of a card.
/// </summary>
internal sealed record UpdateCardRequest(
    string Title,
    decimal OwnPriority,
    IReadOnlyList<string> DeclaredScopeFiles,
    long ExpectedRevision,
    string? Size = null);

/// <summary>
/// Writes a card artifact. A null version creates or overwrites without a conflict check.
/// </summary>
internal sealed record UpdateArtifactRequest(
    string Path,
    string Content,
    string? ExpectedVersion);

/// <summary>
/// Replaces the stages of a workflow.
/// </summary>
internal sealed record UpdateWorkflowRequest(
    string Title,
    IReadOnlyList<StageDefinition> Stages,
    long ExpectedRevision);

/// <summary>
/// Error body of the daemon API.
/// </summary>
internal sealed record ErrorResponse(string Message);

/// <summary>
/// Identification of the running daemon (a superset of the fields the daemon returns).
/// </summary>
internal sealed record SystemInfo(
    string Name,
    string Version,
    string Runtime,
    int ProcessId,
    string BaseUrl,
    DateTimeOffset Time);

/// <summary>
/// A freshly issued one-time pairing code. The browser builds the self-pairing URL from it
/// (<c>#pair=&lt;code&gt;</c>), which is what <c>aiko pair</c> prints for a second machine.
/// </summary>
internal sealed record PairCodeResponse(string Code);
