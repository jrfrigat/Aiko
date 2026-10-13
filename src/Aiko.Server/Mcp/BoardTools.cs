using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aiko.Application.Contracts;
using Aiko.Application.Prioritization;
using Aiko.Domain.Workflow;
using Aiko.Server.Contracts;
using ModelContextProtocol.Server;

namespace Aiko.Server.Mcp;

/// <summary>
/// MCP tool that reads the project's board: the same snapshot the interface draws.
/// </summary>
/// <remarks>
/// An agent that walks the board needs the order the board is in, and that order is a computed priority -
/// the task's own weight mixed with its parent's - not the card's <c>OwnPriority</c>. Sorting the cards by
/// hand would pick a different first card than the one at the top of the screen, so the snapshot is
/// assembled here by the same code that serves <c>/api/v1/projects/{projectId}/board</c>: two ways to
/// compute the board order is exactly the kind of pair that sooner or later disagrees.
/// </remarks>
[McpServerToolType]
internal sealed class BoardTools(
    IHttpContextAccessor httpContextAccessor,
    IProjectCatalog projects,
    IProjectDefinitionStore definitions,
    ICardStore cards,
    IRelationStore relations,
    IAppSettingsService settings,
    IExecutionCoordinator executions) : ProjectToolBase(httpContextAccessor, projects)
{
    [McpServerTool(Name = "aiko_list_board", Title = "Read the Aiko board")]
    [Description(
        "Reads the project's board: the cards, their relations, the priority each one is shown with and where "
        + "each stage got to. Read it when the order of the work matters - the board's priority is computed "
        + "from the card and its parent, not the card's own score.")]
    public async Task<string> ListBoardAsync(
        [Description(
            "True to answer with the whole cards; false (the default) with the thin ones - id, type, stage, "
            + "size and priority. Read one card whole with aiko_get_card.")]
        bool fullCards = false,
        CancellationToken cancellationToken = default)
    {
        var projectId = GetProjectId();
        var project = await GetProjectAsync(cancellationToken);
        var definition = await definitions.ReadAsync(projectId, cancellationToken);
        var projectCards = await cards.ListAsync(projectId, cancellationToken);
        // The same two lists the screen is served: the board, and the archive beside it. The archive is not
        // part of Cards, so a counter or a priority derived from this snapshot never sees a card put away.
        var boardCards = CardArchiving.OnBoard(projectCards);
        var boardRelations = await relations.ListAsync(projectId, cancellationToken);
        var priority = await settings.GetEffectivePriorityAsync(projectId, cancellationToken);
        var snapshot = new ProjectBoardSnapshot(
            project,
            definition.Workflows,
            definition.Projections,
            boardCards,
            boardRelations,
            CardPriorityProjector.Project(boardCards, boardRelations, priority, definition.Workflows),
            await executions.ReadStageRunsAsync(projectId, cancellationToken),
            [.. projectCards.Where(card => card.IsArchived)]);
        var answer = JsonSerializer
            .SerializeToNode(snapshot, ServerJsonContext.Default.ProjectBoardSnapshot)!.AsObject();
        if (!fullCards)
        {
            // The board's own cards are thinned where they stand: its relations, its priorities and the stage
            // runs beside them are the board, and only what each card says about itself is what a reader of the
            // board does not need.
            foreach (var name in new[] { "cards", "archivedCards" })
            {
                if (answer.TryGetPropertyValue(name, out var node) && node is JsonArray cards)
                {
                    answer[name] = CardAnswer.Thin(cards);
                }
            }
        }

        return answer.ToJsonString();
    }
}
