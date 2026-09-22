using Aiko.Application.Prioritization;
using Aiko.Domain.Cards;
using Aiko.Domain.Workflow;

namespace Aiko.Application.Contracts;

/// <summary>
/// Full board snapshot of a project for the UI: definitions, cards, relations
/// and the effective priorities of every card.
/// </summary>
/// <param name="Project">The project the snapshot belongs to.</param>
/// <param name="Workflows">The project's pipelines, which are also its card types.</param>
/// <param name="Projections">The board projections the project offers.</param>
/// <param name="Cards">Every card on the board: the ones that are not in the archive.</param>
/// <param name="Relations">Every relation between its cards.</param>
/// <param name="CardPriorities">The effective priority of every card on the board.</param>
/// <param name="StageRuns">
/// The latest run of each (card, stage) pair, so a board can show where a card's stage got to without asking
/// for every card's history. Null when the publisher says nothing about runs, which is what an older daemon
/// sends; a screen treats that as "nothing known".
/// </param>
/// <param name="ArchivedCards">
/// The cards that were put away. They travel beside the board rather than among its cards, because a board
/// draws work and not history: a column, a counter and a priority must not see a card that is in the archive.
/// Null when the publisher says nothing about the archive, which is what an older daemon sends; a screen treats
/// that as "the archive is empty".
/// </param>
public sealed record ProjectBoardSnapshot(
    RegisteredProject Project,
    IReadOnlyList<WorkflowDefinition> Workflows,
    IReadOnlyList<BoardProjectionDefinition> Projections,
    IReadOnlyList<Card> Cards,
    IReadOnlyList<CardRelation> Relations,
    IReadOnlyList<CardPriority> CardPriorities,
    IReadOnlyList<StageRunSummary>? StageRuns = null,
    IReadOnlyList<Card>? ArchivedCards = null);
