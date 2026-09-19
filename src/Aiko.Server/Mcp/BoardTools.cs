using System.ComponentModel;
using System.Text.Json;
using Aiko.Application.Contracts;
using Aiko.Application.Prioritization;
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
        "Reads the project's board: the cards, their relations, the priority each one is shown with, and "
        + "where each stage got to. Read it when the order of the work matters - for example before walking "
        + "the board with /aiko-run-all - because the board's priority is computed from the card and its "
        + "parent, not the card's own score.")]
    public async Task<string> ListBoardAsync(CancellationToken cancellationToken)
    {
        var projectId = GetProjectId();
        var project = await GetProjectAsync(cancellationToken);
        var definition = await definitions.ReadAsync(projectId, cancellationToken);
        var boardCards = await cards.ListAsync(projectId, cancellationToken);
        var boardRelations = await relations.ListAsync(projectId, cancellationToken);
        var priority = await settings.GetEffectivePriorityAsync(projectId, cancellationToken);
        var snapshot = new ProjectBoardSnapshot(
            project,
            definition.Workflows,
            definition.Projections,
            boardCards,
            boardRelations,
            CardPriorityProjector.Project(boardCards, boardRelations, priority, definition.Workflows),
            await executions.ReadStageRunsAsync(projectId, cancellationToken));
        return JsonSerializer.Serialize(snapshot, ServerJsonContext.Default.ProjectBoardSnapshot);
    }
}
