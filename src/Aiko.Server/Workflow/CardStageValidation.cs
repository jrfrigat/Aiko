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
    /// The stages of the card's own workflow, in pipeline order - what both the stage checks and the progress
    /// rule read. An unknown workflow answers with an empty list rather than throwing: a card whose workflow
    /// was renamed away is a card no stage of this project can hold.
    /// </summary>
    public static async ValueTask<IReadOnlyList<StageDefinition>> ReadStagesAsync(
        Card card,
        IProjectDefinitionStore definitions,
        CancellationToken cancellationToken)
    {
        var definition = await definitions.ReadAsync(card.Reference.ProjectId, cancellationToken);
        var workflow = definition.Workflows.FirstOrDefault(item =>
            StringComparer.Ordinal.Equals(item.Id, card.WorkflowId));
        return workflow is null
            ? []
            : workflow.Stages.OrderBy(stage => stage.Order).ToArray();
    }

    /// <summary>
    /// The stage among <paramref name="stages"/> when it exists and allows the card kind; otherwise null.
    /// </summary>
    public static StageDefinition? FindValidStage(
        IReadOnlyList<StageDefinition> stages,
        Card card,
        string stageId) =>
        stages.FirstOrDefault(stage =>
            StringComparer.Ordinal.Equals(stage.Id, stageId) &&
            stage.AllowedCardKinds.Contains(card.Kind));

    /// <summary>
    /// Returns the target stage when it exists in the card's workflow and allows the card kind;
    /// otherwise returns null.
    /// </summary>
    public static async ValueTask<StageDefinition?> FindValidStageAsync(
        Card card,
        string stageId,
        IProjectDefinitionStore definitions,
        CancellationToken cancellationToken) =>
        FindValidStage(await ReadStagesAsync(card, definitions, cancellationToken), card, stageId);
}
