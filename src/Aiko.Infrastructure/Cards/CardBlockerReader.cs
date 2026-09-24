using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
using Aiko.Domain.Workflow;

namespace Aiko.Infrastructure.Cards;

/// <summary>
/// <see cref="ICardBlockers"/> over the project's own files: relations.json for the edges, and the cards and
/// workflows for what an edge means.
/// </summary>
public sealed class CardBlockerReader(
    IProjectCatalog catalog,
    ICardStore cards,
    IRelationStore relations,
    IProjectDefinitionStore definitions,
    IExecutionCoordinator executions) : ICardBlockers
{
    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<CardBlocker>> UnfinishedAsync(
        CardReference card,
        CancellationToken cancellationToken)
    {
        // The call may arrive through the project's readable handle - the MCP endpoint carries one - while the
        // stores are keyed by the immutable id. Resolving here is the same guard the relation store applies on
        // write, and it keeps one project's answer from being read out of another's files.
        var project = await catalog.FindAsync(card.ProjectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Unknown Aiko project: {card.ProjectId}");
        var reference = new CardReference(project.Id, card.CardId);

        var projectRelations = await relations.ListAsync(reference.ProjectId, cancellationToken);
        var incoming = projectRelations.Any(relation =>
            StringComparer.Ordinal.Equals(relation.Type, RelationTypes.Blocks) &&
            StringComparer.Ordinal.Equals(relation.Target.ProjectId, reference.ProjectId) &&
            StringComparer.Ordinal.Equals(relation.Target.CardId, reference.CardId));
        if (!incoming)
        {
            // The common case by far: no edge points at this card, so its cards and workflows are not read at
            // all and starting an ordinary card costs one relations read more than it did.
            return [];
        }

        var projectCards = await cards.ListAsync(reference.ProjectId, cancellationToken);
        var definition = await definitions.ReadAsync(reference.ProjectId, cancellationToken);
        var runs = await executions.ReadStageRunsAsync(reference.ProjectId, cancellationToken);
        return CardBlocking.Unfinished(
            reference,
            projectRelations,
            projectCards,
            definition.Workflows,
            blocker => runs.FirstOrDefault(run =>
                StringComparer.Ordinal.Equals(run.CardId, blocker.Reference.CardId) &&
                StringComparer.Ordinal.Equals(run.StageId, blocker.StageId))?.StateValue);
    }
}
