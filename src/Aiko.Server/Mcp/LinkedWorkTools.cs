using System.ComponentModel;
using System.Text.Json;
using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
using Aiko.Domain.Execution;
using Aiko.Domain.Workflow;
using Aiko.Server.Contracts;
using ModelContextProtocol.Server;

namespace Aiko.Server.Mcp;

/// <summary>
/// MCP tool over the linked projects: what waits there for this project, and what came from it.
/// </summary>
/// <remarks>
/// The neighbours are read as the daemon reads any project - the same card store, the same definitions, the same
/// run summaries - so the federated view cannot disagree with the board it was read from. The hand-over itself
/// still happens through <c>aiko_create_card_in_project</c>; this tool only shows the state around it.
/// </remarks>
[McpServerToolType]
internal sealed class LinkedWorkTools(
    IHttpContextAccessor httpContextAccessor,
    IProjectCatalog projects,
    ICardStore cards,
    IProjectLinkStore links,
    IProjectDefinitionStore definitions,
    IAppSettingsService settings,
    IExecutionCoordinator executions) : ProjectToolBase(httpContextAccessor, projects)
{
    [McpServerTool(Name = "aiko_list_linked_work", Title = "Read the Aiko linked work")]
    [Description(
        "Lists, for the projects this one is linked to, the cards handed over to a neighbour and still there "
        + "(sent) and the cards this project received from one (received), each with its stage and the card "
        + "it came from. Every entry carries the cross-project policy (deny/ask/allow) and whether that policy "
        + "lets a hand-over to that neighbour, so a caller sees it before it tries one. Read-only, and the "
        + "neighbours are the projects registered with this daemon.")]
    public async Task<string> ListLinkedWorkAsync(CancellationToken cancellationToken = default)
    {
        var current = await GetProjectAsync(cancellationToken);
        var crossProject = await settings.GetEffectiveCrossProjectAsync(current.Id, cancellationToken);
        var policy = crossProject.WritePolicy.ToString().ToLowerInvariant();
        // This project's own board is read once: every neighbour compares against the same view of it.
        var currentCards = CardArchiving.OnBoard(await cards.ListAsync(current.Id, cancellationToken));
        var currentDefinition = await definitions.ReadAsync(current.Id, cancellationToken);
        var currentRuns = await executions.ReadStageRunsAsync(current.Id, cancellationToken);

        var result = new List<LinkedProjectWork>();
        foreach (var link in await links.ListAsync(current.Id, cancellationToken))
        {
            try
            {
                var neighbourCards = CardArchiving.OnBoard(
                    await cards.ListAsync(link.ProjectId, cancellationToken));
                var neighbourDefinition = await definitions.ReadAsync(link.ProjectId, cancellationToken);
                var neighbourRuns = await executions.ReadStageRunsAsync(link.ProjectId, cancellationToken);

                result.Add(new LinkedProjectWork(
                    link.ProjectId,
                    link.Handle,
                    policy,
                    Allows(crossProject, link),
                    Items(
                        neighbourCards.Where(card =>
                            Matches(card.Origin?.ProjectId, current.Id, current.Handle)),
                        neighbourDefinition,
                        neighbourRuns),
                    Items(
                        currentCards.Where(card =>
                            Matches(card.Origin?.ProjectId, link.ProjectId, link.Handle)),
                        currentDefinition,
                        currentRuns)));
            }
            catch (KeyNotFoundException)
            {
                // A link whose project is no longer registered is skipped rather than failing the read.
            }
        }

        return JsonSerializer.Serialize(result, ServerJsonContext.Default.IReadOnlyListLinkedProjectWork);
    }

    private static IReadOnlyList<LinkedWorkItem> Items(
        IEnumerable<Card> fromCards,
        ProjectBoardDefinition definition,
        IReadOnlyList<StageRunSummary> runs) =>
        [.. fromCards
            .OrderBy(card => card.Reference.CardId, StringComparer.Ordinal)
            .Select(card =>
            {
                var workflow = definition.Workflows.FirstOrDefault(candidate =>
                    StringComparer.Ordinal.Equals(candidate.Id, card.WorkflowId));
                var stage = workflow?.Stages.FirstOrDefault(candidate =>
                    StringComparer.Ordinal.Equals(candidate.Id, card.StageId));
                var run = runs.FirstOrDefault(candidate =>
                    StringComparer.Ordinal.Equals(candidate.CardId, card.Reference.CardId) &&
                    StringComparer.Ordinal.Equals(candidate.StageId, card.StageId));
                return new LinkedWorkItem(
                    card.Reference.CardId,
                    card.Title,
                    card.StageId,
                    stage?.Title ?? card.StageId,
                    run?.State ?? "Pending",
                    CardCompletion.IsFinished(workflow, card.StageId, run?.StateValue),
                    card.Origin?.ProjectId,
                    card.Origin?.CardId);
            })];

    /// <summary>
    /// Whether an origin mark names this project. The mark records what the caller passed to the hand-over, so
    /// it may be the project's immutable id or its readable handle; both are accepted.
    /// </summary>
    private static bool Matches(string? originProjectId, string projectId, string projectHandle) =>
        !string.IsNullOrWhiteSpace(originProjectId) &&
        (StringComparer.OrdinalIgnoreCase.Equals(originProjectId, projectId) ||
         StringComparer.OrdinalIgnoreCase.Equals(originProjectId, projectHandle));

    /// <summary>
    /// Whether this project's policy lets a card be handed to that neighbour - the same reading a hand-over
    /// applies before it writes.
    /// </summary>
    private static bool Allows(CrossProjectSettings policy, ProjectLink link) =>
        policy.WritePolicy != CrossProjectWritePolicy.Deny &&
        (policy.Targets.Count == 0 ||
         policy.Targets.Contains(link.ProjectId, StringComparer.OrdinalIgnoreCase) ||
         policy.Targets.Contains(link.Handle, StringComparer.OrdinalIgnoreCase));
}
