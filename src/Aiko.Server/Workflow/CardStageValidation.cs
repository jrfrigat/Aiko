using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
using Aiko.Domain.Workflow;

namespace Aiko.Server.Workflow;

/// <summary>
/// Shared validation of a card's stage transition against its workflow definition, used by both
/// the REST endpoints and the MCP tools so they apply the same rules.
/// </summary>
internal static class CardStageValidation
{
    /// <summary>
    /// Returns the target stage when it exists in the card's workflow and allows the card kind;
    /// otherwise returns null.
    /// </summary>
    public static async ValueTask<StageDefinition?> FindValidStageAsync(
        Card card,
        string stageId,
        IProjectDefinitionStore definitions,
        CancellationToken cancellationToken)
    {
        var definition = await definitions.ReadAsync(card.Reference.ProjectId, cancellationToken);
        var workflow = definition.Workflows.FirstOrDefault(workflow =>
            StringComparer.Ordinal.Equals(workflow.Id, card.WorkflowId));
        var stage = workflow?.Stages.FirstOrDefault(item =>
            StringComparer.Ordinal.Equals(item.Id, stageId));
        return stage is not null && stage.AllowedCardKinds.Contains(card.Kind) ? stage : null;
    }
}
