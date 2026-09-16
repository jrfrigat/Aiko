using Aiko.Application.Prioritization;
using Aiko.Domain.Cards;
using Aiko.Domain.Workflow;

namespace Aiko.Application.Contracts;

/// <summary>
/// Full board snapshot of a project for the UI: definitions, cards, relations
/// and the effective priorities of every card.
/// </summary>
public sealed record ProjectBoardSnapshot(
    RegisteredProject Project,
    IReadOnlyList<WorkflowDefinition> Workflows,
    IReadOnlyList<BoardProjectionDefinition> Projections,
    IReadOnlyList<Card> Cards,
    IReadOnlyList<CardRelation> Relations,
    IReadOnlyList<CardPriority> CardPriorities);
