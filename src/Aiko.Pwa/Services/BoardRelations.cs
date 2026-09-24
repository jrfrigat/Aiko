using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
using Aiko.Domain.Workflow;

namespace Aiko.Pwa.Services;

/// <summary>
/// What the board says about a card's links that a plain list of ids cannot: which cards hold it back.
/// </summary>
/// <remarks>
/// The blockers are read with the domain's own rule, <see cref="CardBlocking.Unfinished"/>, over the snapshot's
/// latest runs - the rule the start gate applies - so the board never shows a card as blocked that an agent
/// could start, or free when an agent would be refused.
/// </remarks>
public static class BoardRelations
{
    /// <summary>The cards that still hold <paramref name="card"/> back, by id.</summary>
    /// <param name="card">The card on the board.</param>
    /// <param name="relations">The snapshot's relations.</param>
    /// <param name="cards">The cards the snapshot carries, so a blocker can be named.</param>
    /// <param name="workflows">The project's workflows, so the end of a blocker's pipeline can be read.</param>
    /// <param name="runs">The latest run of each card's stages.</param>
    public static IReadOnlyList<CardBlocker> BlockedBy(
        Card card,
        IReadOnlyList<CardRelation> relations,
        IReadOnlyList<Card> cards,
        IReadOnlyList<WorkflowDefinition> workflows,
        IReadOnlyList<StageRunSummary> runs)
    {
        ArgumentNullException.ThrowIfNull(card);
        return CardBlocking.Unfinished(
            card.Reference,
            relations,
            cards,
            workflows,
            blocker => runs.FirstOrDefault(run =>
                StringComparer.Ordinal.Equals(run.CardId, blocker.Reference.CardId) &&
                StringComparer.Ordinal.Equals(run.StageId, blocker.StageId))?.StateValue);
    }
}
