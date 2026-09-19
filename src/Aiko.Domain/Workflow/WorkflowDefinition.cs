using System.Text.Json.Serialization;
using Aiko.Domain.Cards;

namespace Aiko.Domain.Workflow;

/// <summary>
/// A project workflow: an ordered set of stages with an optimistic revision.
/// </summary>
/// <remarks>
/// A workflow is also the definition of one card type: its stages say what a card of that type must pass
/// through, and its presentation says how the type is drawn. The type id is the workflow id, so the two are
/// the same decision rather than two that can disagree.
/// </remarks>
/// <param name="Id">Stable ASCII identifier; also the card type id, for example <c>story</c>.</param>
/// <param name="Title">Display name of the workflow and of the card type it defines.</param>
/// <param name="Stages">The pipeline stages, in the order they run.</param>
/// <param name="Revision">Optimistic concurrency revision.</param>
/// <param name="Description">
/// What the card type is for, in the words of the project. This is where a user who adds an <c>Epic</c>
/// type writes "a global card type that groups several stories".
/// </param>
/// <param name="Icon">
/// Which icon the card type draws, as an id from <see cref="AppearanceCatalog.Icons"/>, or null for the
/// default of the screen that draws it.
/// </param>
/// <param name="Color">
/// Which accent colour the card type draws, as an id from <see cref="AppearanceCatalog.Colors"/>, or null
/// for the default.
/// </param>
/// <param name="BlendsWithParent">
/// Whether a card of this type blends its own score with the maximum parent value, or keeps its own score.
/// The behaviour belongs to the type, so it is declared here as data: a project picks it in the workflow
/// editor, and the priority projector reads this flag instead of comparing a card's kind to a name.
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
    /// The id of the stage every workflow starts its cards in. Backlog is not a column a user may remove:
    /// it is the list of cards that have not been taken into work yet, so a pipeline without it would have
    /// nowhere to put a new card.
    /// </summary>
    public const string BacklogStageId = "backlog";

    /// <summary>
    /// The card type this workflow defines: the workflow id, capitalised. Derived, so it is not written to
    /// the document - the id it comes from already is.
    /// </summary>
    [JsonIgnore]
    public string CardType => CardKind.FromWorkflowId(Id);

    /// <summary>Whether a stage is the reserved backlog stage.</summary>
    /// <param name="stage">Stage to test.</param>
    public static bool IsBacklog(StageDefinition stage) =>
        StringComparer.Ordinal.Equals(stage.Id, BacklogStageId);

    /// <summary>
    /// Whether a stage is the last one of its pipeline: the stage a card reaches when its type's work is done.
    /// </summary>
    /// <remarks>
    /// Asked by the blocking rule, which decides whether a card that blocks another one is finished - the last
    /// stage is read from the workflow because pipelines differ per card type, and calling the end <c>done</c>
    /// is only true of the workflows that happen to use that id.
    /// </remarks>
    /// <param name="workflow">The workflow to test against.</param>
    /// <param name="stage">The stage to place.</param>
    public static bool IsLastStage(WorkflowDefinition workflow, StageDefinition stage)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(stage);

        var last = workflow.Stages
            .OrderByDescending(candidate => candidate.Order)
            .FirstOrDefault();
        return last is not null && StringComparer.Ordinal.Equals(last.Id, stage.Id);
    }
}

