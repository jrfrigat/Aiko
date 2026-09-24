using Aiko.Domain.Workflow;
using Flare.Icons;

namespace Aiko.Pwa.Services;

/// <summary>
/// How a stage is drawn wherever a screen names it: by its title, with the icon and the colour its settings give
/// it.
/// </summary>
/// <remarks>
/// The strings are translated by <see cref="CardAppearance"/>, the same translation the Workflow picker shows, so
/// what is set there is what the card page and "active runs" draw. A stage that names no icon or colour - or one
/// this client does not know - keeps the look its tag already has rather than a default invented by position.
/// </remarks>
public static class StageAppearance
{
    /// <summary>The stage a card sits in, from the card's own workflow, or null when the workflow no longer has it.</summary>
    public static StageDefinition? Find(IEnumerable<WorkflowDefinition>? workflows, string? workflowId, string? stageId) =>
        workflows?
            .FirstOrDefault(workflow => StringComparer.Ordinal.Equals(workflow.Id, workflowId))
            ?.Stages.FirstOrDefault(stage => StringComparer.Ordinal.Equals(stage.Id, stageId));

    /// <summary>The stage's title, or its id when the workflow no longer knows the stage.</summary>
    public static string Caption(StageDefinition? stage, string stageId) => stage?.Title ?? stageId;

    /// <summary>The icon the stage's settings name, or null when they name none this client knows.</summary>
    public static FlareIcon? Icon(StageDefinition? stage) => CardAppearance.Icon(stage?.Icon);

    /// <summary>
    /// The inline tint of a stage tag - the colour as text over a faint wash of itself, the way the product's own
    /// accent tags are drawn - or null, which leaves the tag's class colour in place.
    /// </summary>
    public static string? TagStyle(StageDefinition? stage) =>
        CardAppearance.Color(stage?.Color)?.CssValue is { } color
            ? $"color: {color}; background: color-mix(in srgb, {color} 16%, transparent)"
            : null;
}
