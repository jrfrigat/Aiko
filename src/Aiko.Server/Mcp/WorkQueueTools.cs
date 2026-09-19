using System.ComponentModel;
using System.Text.Json;
using Aiko.Application.Contracts;
using Aiko.Application.Prioritization;
using Aiko.Domain.Workflow;
using Aiko.Server.Contracts;
using ModelContextProtocol.Server;

namespace Aiko.Server.Mcp;

/// <summary>
/// MCP tool that reads the project's work queue: the cards to work, in the order the board ranks them.
/// </summary>
/// <remarks>
/// The queue is a view of the board, not a second opinion about it. The order comes from
/// <see cref="CardPriorityProjector"/> - the same call the board makes - and the blockers from
/// <see cref="CardBlocking"/>, the rule the start gate already enforces. An agent that assembled either of
/// those itself would be re-deriving a rule that lives in one place, which is how a queue starts disagreeing
/// with the board it was read from.
/// <para>
/// It exists because work needs an order: <c>aiko_list_board</c> answers "what is on the board", and a pass
/// over the board asks "what do I pick next". Answering that from the snapshot meant re-implementing the
/// priority blend and the blocking rule on the agent's side.
/// </para>
/// </remarks>
[McpServerToolType]
internal sealed class WorkQueueTools(
    IHttpContextAccessor httpContextAccessor,
    IProjectCatalog projects,
    ICardStore cards,
    IRelationStore relations,
    IProjectDefinitionStore definitions,
    IAppSettingsService settings,
    IExecutionCoordinator executions) : ProjectToolBase(httpContextAccessor, projects)
{
    [McpServerTool(Name = "aiko_list_work_queue", Title = "Read the Aiko work queue")]
    [Description(
        "Lists the cards to work, in the order the board ranks them: effective priority first, then the card "
        + "id. Each entry carries the card's type, stage, size and scores, the cards that block it, the state "
        + "of its current stage and whether it is finished. Read this instead of assembling an order from "
        + "aiko_list_board: the priority blend and the blocking rule are applied here, by the code that owns "
        + "them. This is the queue of cards, not the queue of commands a screen placed (aiko_list_commands).")]
    public async Task<string> ListWorkQueueAsync(
        [Description("Only cards of this type, or null for every type the project defines.")]
        string? kind = null,
        [Description(
            "True to include cards that already reached the end of their pipeline. False (the default) keeps "
            + "the queue to what still has work in it.")]
        bool includeFinished = false,
        CancellationToken cancellationToken = default)
    {
        var projectId = GetProjectId();
        var boardCards = await cards.ListAsync(projectId, cancellationToken);
        var boardRelations = await relations.ListAsync(projectId, cancellationToken);
        var definition = await definitions.ReadAsync(projectId, cancellationToken);
        var priority = await settings.GetEffectivePriorityAsync(projectId, cancellationToken);
        // One read of each source: the blockers are then resolved in memory against the same relations,
        // cards and workflows, rather than once per card.
        var effectivePriorities = CardPriorityProjector
            .Project(boardCards, boardRelations, priority, definition.Workflows)
            .ToDictionary(
                item => item.CardId,
                item => item.Snapshot.EffectivePriority,
                StringComparer.Ordinal);
        var runsByCard = (await executions.ReadStageRunsAsync(projectId, cancellationToken))
            .GroupBy(run => run.CardId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        var wantedKind = string.IsNullOrWhiteSpace(kind) ? null : ParseCardKind(kind);
        var entries = new List<WorkQueueEntry>();
        foreach (var card in boardCards)
        {
            if (wantedKind is not null &&
                !StringComparer.OrdinalIgnoreCase.Equals(card.Kind, wantedKind))
            {
                continue;
            }

            var workflow = definition.Workflows.FirstOrDefault(candidate =>
                StringComparer.Ordinal.Equals(candidate.Id, card.WorkflowId));
            var stage = workflow?.Stages.FirstOrDefault(candidate =>
                StringComparer.Ordinal.Equals(candidate.Id, card.StageId));
            var runs = runsByCard.TryGetValue(card.Reference.CardId, out var cardRuns)
                ? cardRuns
                : [];
            var currentRun = runs.FirstOrDefault(run =>
                StringComparer.Ordinal.Equals(run.StageId, card.StageId));
            // Finished is where the pipeline ends rather than a matter of opinion: the card sits in the last
            // stage of its own workflow and that stage has a finished run. A card in a middle stage with a
            // finished run is still work - the next stage is what it waits for.
            var finished = workflow is not null &&
                           stage is not null &&
                           WorkflowDefinition.IsLastStage(workflow, stage) &&
                           currentRun is { State: "Completed" };
            if (finished && !includeFinished)
            {
                continue;
            }

            entries.Add(new WorkQueueEntry(
                card.Reference.CardId,
                card.Title,
                card.Kind,
                card.WorkflowId,
                card.StageId,
                stage?.Title ?? card.StageId,
                card.Size,
                card.OwnPriority,
                effectivePriorities.TryGetValue(card.Reference.CardId, out var effective)
                    ? effective
                    : card.OwnPriority,
                CardBlocking.Unfinished(card.Reference, boardRelations, boardCards, definition.Workflows),
                currentRun?.State ?? "Pending",
                finished));
        }

        var ordered = entries
            .OrderByDescending(entry => entry.EffectivePriority)
            .ThenBy(entry => entry.CardId, StringComparer.Ordinal)
            .ToArray();
        return JsonSerializer.Serialize(
            ordered,
            ServerJsonContext.Default.IReadOnlyListWorkQueueEntry);
    }
}
