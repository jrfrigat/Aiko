namespace Aiko.Application.Contracts;

/// <summary>
/// One card of a linked project's work, as the federated queue names it: where it sits, and - when it was
/// handed over - the card it came from.
/// </summary>
/// <param name="CardId">The card's id in the project it lives in.</param>
/// <param name="Title">Its title.</param>
/// <param name="StageId">The stage it sits in now.</param>
/// <param name="StageTitle">That stage's title, or the id when the workflow cannot be read.</param>
/// <param name="StageState">
/// State of the latest run of that stage, as <c>StageRunSummary</c> writes it, or <c>Pending</c> when the
/// stage was never started.
/// </param>
/// <param name="Finished">True when the card reached the end of its own pipeline.</param>
/// <param name="OriginProjectId">The project the card came from, or null when it was made where it lives.</param>
/// <param name="OriginCardId">The card it came from in that project, or null.</param>
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
/// What one linked project holds for this one: the cards handed over to it and still there (sent), the cards
/// this project received from it (received), and the cross-project policy that governs a hand-over.
/// </summary>
/// <remarks>
/// A view across locally registered projects, not a second store: every field is read from the neighbour's own
/// board, and the policy is the one the sending project states. Nothing here is written or cached.
/// </remarks>
/// <param name="ProjectId">Immutable id of the linked project.</param>
/// <param name="ProjectHandle">The handle it is addressed by.</param>
/// <param name="WritePolicy">This project's cross-project policy - <c>deny</c>, <c>ask</c> or <c>allow</c>.</param>
/// <param name="WriteAllowed">
/// Whether that policy lets a card be handed to this neighbour: the same reading a hand-over applies before it
/// writes, so a caller sees it before it tries.
/// </param>
/// <param name="Sent">Cards this project handed over that are still in the neighbour.</param>
/// <param name="Received">Cards in this project that came from the neighbour.</param>
public sealed record LinkedProjectWork(
    string ProjectId,
    string ProjectHandle,
    string WritePolicy,
    bool WriteAllowed,
    IReadOnlyList<LinkedWorkItem> Sent,
    IReadOnlyList<LinkedWorkItem> Received);
