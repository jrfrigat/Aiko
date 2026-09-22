using Aiko.Application.Contracts;
using Aiko.Application.Prioritization;
using Aiko.Domain.Cards;
using Aiko.Domain.Workflow;

namespace Aiko.Server.Endpoints;

/// <summary>
/// Board endpoints: the full board snapshot of a project.
/// </summary>
internal static class BoardEndpoints
{
    /// <summary>
    /// Maps /api/v1/projects/{projectId}/board.
    /// </summary>
    public static void MapBoardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/api/v1/projects/{projectId}/board",
            async (
                string projectId,
                IProjectCatalog catalog,
                IProjectDefinitionStore definitions,
                ICardStore cards,
                IRelationStore relations,
                IAppSettingsService settings,
                IExecutionCoordinator executions,
                CancellationToken cancellationToken) =>
            {
                var project = await catalog.FindAsync(projectId, cancellationToken);
                if (project is null)
                {
                    return Results.NotFound();
                }

                var definition = await definitions.ReadAsync(projectId, cancellationToken);
                var projectCards = await cards.ListAsync(projectId, cancellationToken);
                // The board draws work, not history. The archive is carried beside it rather than among its
                // cards, so a column, a counter and a priority never see a card that was put away.
                var boardCards = CardArchiving.OnBoard(projectCards);
                var boardRelations = await relations.ListAsync(projectId, cancellationToken);
                var priority = await settings.GetEffectivePriorityAsync(projectId, cancellationToken);
                return Results.Ok(new ProjectBoardSnapshot(
                    project,
                    definition.Workflows,
                    definition.Projections,
                    boardCards,
                    boardRelations,
                    CardPriorityProjector.Project(boardCards, boardRelations, priority, definition.Workflows),
                    // Where each card's stage got to, so the board can show it without a request per card.
                    await executions.ReadStageRunsAsync(projectId, cancellationToken),
                    [.. projectCards.Where(card => card.IsArchived)]));
            });
    }
}
