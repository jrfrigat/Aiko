namespace Aiko.Application.Contracts;

/// <summary>
/// One card of a linked project's work, as the federated queue names it: where it sits, and - when it was
/// handed over - the card it came from. <c>StageTitle</c> falls back to the id when the workflow cannot be
/// read, <c>StageState</c> is <c>Pending</c> for a stage never started, and the origin fields are null for a
/// card made where it lives.
/// </summary>
public sealed record LinkedWorkItem(
    string CardId,
    string Title,
    string StageId,
    string StageTitle,
    string StageState,
    bool Finished,
    string? OriginProjectId,
    string? OriginCardId);

/// <summary>
/// What one linked project holds for this one: the cards handed over to it and still there, the cards this
/// project received from it, and the cross-project policy that governs a hand-over - <c>WritePolicy</c> is
/// <c>deny</c>, <c>ask</c> or <c>allow</c>, and <c>WriteAllowed</c> reads it the same way a hand-over does
/// before writing. A view across locally registered projects, not a second store: every field is read from the
/// neighbour's own board, and nothing here is written or cached.
/// </summary>
public sealed record LinkedProjectWork(
    string ProjectId,
    string ProjectHandle,
    string WritePolicy,
    bool WriteAllowed,
    IReadOnlyList<LinkedWorkItem> Sent,
    IReadOnlyList<LinkedWorkItem> Received);
