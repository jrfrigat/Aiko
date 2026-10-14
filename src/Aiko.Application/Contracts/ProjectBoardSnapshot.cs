using Aiko.Application.Prioritization;
using Aiko.Domain.Cards;
using Aiko.Domain.Workflow;

namespace Aiko.Application.Contracts;

/// <summary>
/// Full board snapshot of a project for the UI: definitions, cards, relations and the effective priorities of
/// every card.
/// </summary>
/// <remarks>
/// <c>StageRuns</c> carries the latest run of each (card, stage) pair, so a board can show where a card's stage
/// got to without asking for every card's history, and <c>ArchivedCards</c> travels beside the board rather
/// than among its cards - a column, a counter and a priority must not see a card that is in the archive. Both
/// are null when an older daemon says nothing about them, and a screen reads that as "nothing known" and "the
/// archive is empty".
/// </remarks>
public sealed record ProjectBoardSnapshot(
    RegisteredProject Project,
    IReadOnlyList<WorkflowDefinition> Workflows,
    IReadOnlyList<BoardProjectionDefinition> Projections,
    IReadOnlyList<Card> Cards,
    IReadOnlyList<CardRelation> Relations,
    IReadOnlyList<CardPriority> CardPriorities,
    IReadOnlyList<StageRunSummary>? StageRuns = null,
    IReadOnlyList<Card>? ArchivedCards = null);
