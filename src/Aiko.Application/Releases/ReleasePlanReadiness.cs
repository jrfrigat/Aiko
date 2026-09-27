using Aiko.Application.Contracts;
using Aiko.Domain.Workflow;

namespace Aiko.Application.Releases;

/// <summary>
/// What one card of a release plan is doing right now.
/// </summary>
/// <remarks>
/// <see cref="Missing"/> is not in the story's vocabulary and is here on purpose: a plan keeps card ids, and a
/// card can be gone - deleted, or moved to a project that is not this one. Reporting such a card as
/// <see cref="InWork"/> would be a wrong answer a reader could not tell from a right one, and a version whose
/// plan names a card nobody can find must not read as releasable.
/// </remarks>
public enum ReleasePlanCardState
{
    /// <summary>The card reached the end of its own pipeline.</summary>
    Finished,

    /// <summary>The card waits for another card that has not finished.</summary>
    Blocked,

    /// <summary>The card exists and still has work in it.</summary>
    InWork,

    /// <summary>The plan names a card this project does not have.</summary>
    Missing
}

/// <summary>
/// One card of a plan, as the release screen reads it: where it got to, and who holds it.
/// </summary>
/// <param name="CardId">Card the plan waits for.</param>
/// <param name="Title">Its current title, or null when the card is missing.</param>
/// <param name="State">Where the card got to.</param>
/// <param name="BlockedBy">
/// The cards that hold it, by id; empty unless <see cref="State"/> is <see cref="ReleasePlanCardState.Blocked"/>.
/// </param>
public sealed record ReleasePlanCardReadiness(
    string CardId,
    string? Title,
    ReleasePlanCardState State,
    IReadOnlyList<string> BlockedBy);

/// <summary>
/// How far a version is from being releasable: the state of each planned card, and what the counts make of it.
/// </summary>
/// <remarks>
/// Derived from the live cards on every read and never stored: a saved status would have to be repaired after
/// every stage a card enters, and the thing that repairs it would be the thing that already knows - so the
/// store would gain a copy of a rule it does not own.
/// </remarks>
/// <param name="Cards">The planned cards, in the order the plan holds them.</param>
/// <param name="Finished">How many of them reached the end of their pipeline.</param>
/// <param name="Blocked">How many wait for another card.</param>
/// <param name="InWork">How many still have work in them.</param>
/// <param name="Missing">How many the project no longer has.</param>
public sealed record ReleasePlanReadiness(
    IReadOnlyList<ReleasePlanCardReadiness> Cards,
    int Finished,
    int Blocked,
    int InWork,
    int Missing)
{
    /// <summary>
    /// Whether every card the plan names has finished, which is what "this version can go" means.
    /// </summary>
    /// <remarks>
    /// A plan with nothing in it is releasable: no card holds the version. That is a state worth reading rather
    /// than an error - a version whose work is all in other versions ships nothing new, and the release record
    /// has always allowed an empty card list for exactly that.
    /// </remarks>
    public bool IsReleasable => Finished == Cards.Count;
}

/// <summary>
/// One plan with how far it is from being releasable.
/// </summary>
/// <param name="Plan">The plan itself.</param>
/// <param name="Readiness">Where its cards got to, read from the live cards.</param>
public sealed record ReleasePlanReport(ReleasePlan Plan, ReleasePlanReadiness Readiness);

/// <summary>
/// Every plan of a project with its readiness, which is what the agent reads before conducting a release.
/// </summary>
/// <param name="Plans">The plans, most recently created first, as the document holds them.</param>
public sealed record ReleasePlanDocumentReport(IReadOnlyList<ReleasePlanReport> Plans);

/// <summary>
/// Reads how far each version is from being releasable, from the cards as they stand.
/// </summary>
/// <remarks>
/// It asks the two questions the rest of Aiko already asks - "did this card reach the end of its pipeline"
/// (<see cref="CardCompletion.IsFinished"/>) and "who holds it" (<see cref="CardBlocking.Unfinished"/>) - rather
/// than forming a third opinion about a card. A second rule would sooner or later tell a person something about
/// a card that the work queue beside it contradicts.
/// <para>
/// Everything needed is read once per call and the per-card questions are answered in memory, the way the work
/// queue does it: a plan of twenty cards must not cost twenty reads of the project.
/// </para>
/// </remarks>
public sealed class ReleasePlanReadinessProjector(
    ICardStore cards,
    IRelationStore relations,
    IProjectDefinitionStore definitions,
    IExecutionCoordinator executions,
    IReleasePlanStore plans)
{
    /// <summary>
    /// Reads the project's plans with the readiness of each.
    /// </summary>
    /// <param name="projectId">Project to read, by id or by its readable handle.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask<ReleasePlanDocumentReport> ReadAsync(
        string projectId,
        CancellationToken cancellationToken)
    {
        var document = await plans.ReadAsync(projectId, cancellationToken);
        if (document.Plans.Count == 0)
        {
            // Nothing is planned, so the cards are not read at all: an empty answer costs nothing.
            return new ReleasePlanDocumentReport([]);
        }

        var projectCards = await cards.ListAsync(projectId, cancellationToken);
        var projectRelations = await relations.ListAsync(projectId, cancellationToken);
        var definition = await definitions.ReadAsync(projectId, cancellationToken);
        var runsByCard = (await executions.ReadStageRunsAsync(projectId, cancellationToken))
            .GroupBy(run => run.CardId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var cardsById = projectCards.ToDictionary(card => card.Reference.CardId, StringComparer.Ordinal);

        return new ReleasePlanDocumentReport(
            [.. document.Plans.Select(plan => new ReleasePlanReport(plan, Read(plan)))]);

        ReleasePlanReadiness Read(ReleasePlan plan)
        {
            var entries = new List<ReleasePlanCardReadiness>(plan.Cards.Count);
            foreach (var entry in plan.Cards)
            {
                if (!cardsById.TryGetValue(entry.CardId.Trim(), out var card))
                {
                    entries.Add(new ReleasePlanCardReadiness(
                        entry.CardId, null, ReleasePlanCardState.Missing, []));
                    continue;
                }

                var workflow = definition.Workflows.FirstOrDefault(candidate =>
                    StringComparer.Ordinal.Equals(candidate.Id, card.WorkflowId));
                var runs = runsByCard.TryGetValue(card.Reference.CardId, out var cardRuns) ? cardRuns : [];
                var currentRun = runs.FirstOrDefault(run =>
                    StringComparer.Ordinal.Equals(run.StageId, card.StageId));
                if (CardCompletion.IsFinished(workflow, card.StageId, currentRun?.StateValue))
                {
                    entries.Add(new ReleasePlanCardReadiness(
                        card.Reference.CardId, card.Title, ReleasePlanCardState.Finished, []));
                    continue;
                }

                // Blocked outranks "in work": a card that cannot be started is a different thing to tell a
                // person than a card nobody has picked up yet.
                var blockers = CardBlocking.Unfinished(
                    card.Reference,
                    projectRelations,
                    projectCards,
                    definition.Workflows,
                    candidate => runsByCard.TryGetValue(candidate.Reference.CardId, out var blockerRuns)
                        ? blockerRuns.FirstOrDefault(run =>
                            StringComparer.Ordinal.Equals(run.StageId, candidate.StageId))?.StateValue
                        : null);
                entries.Add(blockers.Count > 0
                    ? new ReleasePlanCardReadiness(
                        card.Reference.CardId,
                        card.Title,
                        ReleasePlanCardState.Blocked,
                        [.. blockers.Select(blocker => blocker.CardId)])
                    : new ReleasePlanCardReadiness(
                        card.Reference.CardId, card.Title, ReleasePlanCardState.InWork, []));
            }

            return new ReleasePlanReadiness(
                entries,
                entries.Count(entry => entry.State == ReleasePlanCardState.Finished),
                entries.Count(entry => entry.State == ReleasePlanCardState.Blocked),
                entries.Count(entry => entry.State == ReleasePlanCardState.InWork),
                entries.Count(entry => entry.State == ReleasePlanCardState.Missing));
        }
    }
}
