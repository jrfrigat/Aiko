using Aiko.Application.Contracts;
using Aiko.Application.Prioritization;
using Aiko.Domain.Cards;

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
                var boardCards = await cards.ListAsync(projectId, cancellationToken);
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
                    await executions.ReadStageRunsAsync(projectId, cancellationToken)));
            });
    }
}
