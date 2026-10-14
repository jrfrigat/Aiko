using System.Text.Json.Serialization;
using Aiko.Domain.Cards;

namespace Aiko.Domain.Workflow;

/// <summary>
/// A project workflow: an ordered set of stages with an optimistic revision.
/// </summary>
/// <remarks>
/// A workflow is also the definition of one card type: its stages say what such a card passes through, and
/// the type id is the workflow id - one decision, not two that can disagree.
/// </remarks>
/// <param name="Id">Stable ASCII identifier; also the card type id, for example <c>story</c>.</param>
/// <param name="Title">Display name of the workflow and of the card type it defines.</param>
/// <param name="Stages">The pipeline stages, in the order they run.</param>
/// <param name="Revision">Optimistic concurrency revision.</param>
/// <param name="Description">
/// What the card type is for, in the words of the project - where a user who adds an <c>Epic</c> type writes
/// what it groups.
/// </param>
/// <param name="Icon">
/// Icon id from <see cref="AppearanceCatalog.Icons"/>, or null for the default of the screen that draws it.
/// </param>
/// <param name="Color">
/// Accent colour id from <see cref="AppearanceCatalog.Colors"/>, or null for the default.
/// </param>
/// <param name="BlendsWithParent">
/// Whether a card of this type blends its own score with the maximum parent value instead of keeping its own.
/// Declared here as data, so the projector reads a flag rather than comparing a kind to a name.
/// </param>
public sealed record WorkflowDefinition(
    string Id,
    string Title,
    IReadOnlyList<StageDefinition> Stages,
    long Revision,
    string? Description = null,
    string? Icon = null,
    string? Color = null,
    bool BlendsWithParent = false)
{
    /// <summary>
    /// The stage every workflow starts its cards in. Not a column a user may remove: a pipeline without it
    /// would have nowhere to put a new card.
    /// </summary>
    public const string BacklogStageId = "backlog";

    /// <summary>
    /// The stage every workflow ends its cards in, and the only stage a card's work may be called finished in.
    /// Reserved like the backlog: the engine reads the end of a pipeline from its own rule, so a workflow
    /// ending anywhere else has no end at all.
    /// </summary>
    public const string DoneStageId = "done";

    /// <summary>
    /// The card type this workflow defines: the workflow id, capitalised. Derived, so it is not written to the
    /// document.
    /// </summary>
    [JsonIgnore]
    public string CardType => CardKind.FromWorkflowId(Id);

    /// <summary>Whether a stage is the reserved backlog stage.</summary>
    /// <param name="stage">Stage to test.</param>
    public static bool IsBacklog(StageDefinition stage) =>
        StringComparer.Ordinal.Equals(stage.Id, BacklogStageId);

    /// <summary>Whether a stage is the reserved done stage.</summary>
    /// <param name="stage">Stage to test.</param>
    public static bool IsDone(StageDefinition stage) =>
        StringComparer.Ordinal.Equals(stage.Id, DoneStageId);

    /// <summary>
    /// Whether a stage is the end of its pipeline: the stage a card reaches when its type's work is done.
    /// </summary>
    /// <remarks>
    /// The end is the reserved <see cref="DoneStageId"/> stage rather than the stage with the greatest order,
    /// and the workflow has to carry it - which is what makes "finished", "archivable" and "no longer
    /// blocking" mean the same thing however the pipeline was authored.
    /// </remarks>
    /// <param name="workflow">The workflow to test against.</param>
    /// <param name="stage">The stage to place.</param>
    public static bool IsLastStage(WorkflowDefinition workflow, StageDefinition stage)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(stage);

        return IsDone(stage) && workflow.Stages.Any(IsDone);
    }

    /// <summary>
    /// Why the pipeline breaks the reserved-stage rule - it must begin with <see cref="BacklogStageId"/> and end
    /// with <see cref="DoneStageId"/> - or null when it does not.
    /// </summary>
    /// <remarks>
    /// Stated once, here, and asked by both the write path and the read path, so the two cannot say different
    /// things about the same file.
    /// </remarks>
    /// <param name="workflow">The workflow to test.</param>
    public static string? RefuseReservedStages(WorkflowDefinition workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        if (workflow.Stages is null || workflow.Stages.Count == 0)
        {
            return "A workflow must contain at least one stage.";
        }

        var backlog = workflow.Stages.FirstOrDefault(IsBacklog);
        if (backlog is null)
        {
            return $"A workflow must keep its {BacklogStageId} stage.";
        }

        if (backlog.Order != workflow.Stages.Min(stage => stage.Order))
        {
            return $"The {BacklogStageId} stage must be the first stage of the workflow.";
        }

        var done = workflow.Stages.FirstOrDefault(IsDone);
        if (done is null)
        {
            return $"A workflow must keep its {DoneStageId} stage.";
        }

        if (done.Order != workflow.Stages.Max(stage => stage.Order))
        {
            return $"The {DoneStageId} stage must be the last stage of the workflow.";
        }

        return null;
    }
}

