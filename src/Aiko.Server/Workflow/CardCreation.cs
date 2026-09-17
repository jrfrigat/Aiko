using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
using Aiko.Domain.Workflow;

namespace Aiko.Server.Workflow;

/// <summary>
/// The rules a new card is created under, shared by the REST endpoint and both MCP tools so they cannot
/// disagree about what "create a card" means.
/// </summary>
/// <remarks>
/// Two of them are the point. A card is born in the reserved backlog stage of its own pipeline and nowhere
/// else - it has not been worked out yet, so opening it directly in "Implementation" is a claim nobody has
/// made - and its id is invented by Aiko rather than typed, because the id names the card's folder and a
/// person inventing one adds nothing but a chance to collide.
/// </remarks>
internal static class CardCreation
{
    /// <summary>
    /// Resolves the workflow a new card of this type joins, and its backlog stage.
    /// </summary>
    /// <param name="definitions">The project's workflows.</param>
    /// <param name="projectId">Project the card will belong to.</param>
    /// <param name="kind">Card type id, for example <c>Task</c>.</param>
    /// <param name="workflowId">Workflow id, or null to take the type's own id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The workflow and its backlog stage, or the reason the card cannot be created.</returns>
    internal static async ValueTask<(WorkflowDefinition? Workflow, StageDefinition? Backlog, string? Error)>
        ResolveAsync(
            IProjectDefinitionStore definitions,
            string projectId,
            string kind,
            string? workflowId,
            CancellationToken cancellationToken)
    {
        var resolvedId = string.IsNullOrWhiteSpace(workflowId)
            ? CardKind.ToWorkflowId(kind)
            : workflowId.Trim();
        var definition = await definitions.ReadAsync(projectId, cancellationToken);
        var workflow = definition.Workflows.FirstOrDefault(item =>
            StringComparer.Ordinal.Equals(item.Id, resolvedId));
        if (workflow is null)
        {
            return (null, null, $"Unknown workflow: {resolvedId}.");
        }

        var backlog = workflow.Stages.FirstOrDefault(WorkflowDefinition.IsBacklog);
        if (backlog is null)
        {
            return (null, null, $"Workflow '{resolvedId}' has no {WorkflowDefinition.BacklogStageId} stage.");
        }

        if (!backlog.AllowedCardKinds.Contains(kind, StringComparer.OrdinalIgnoreCase))
        {
            return (null, null, $"Workflow '{resolvedId}' does not accept card type '{kind}'.");
        }

        return (workflow, backlog, null);
    }

    /// <summary>
    /// Whether a stage a caller asked for is the backlog stage a card may be created in. Null and blank mean
    /// "the backlog", which is what a caller that does not care about stages passes.
    /// </summary>
    /// <param name="requestedStageId">Stage the caller named, if any.</param>
    public static bool IsCreationStage(string? requestedStageId) =>
        string.IsNullOrWhiteSpace(requestedStageId) ||
        StringComparer.Ordinal.Equals(requestedStageId.Trim(), WorkflowDefinition.BacklogStageId);
}
