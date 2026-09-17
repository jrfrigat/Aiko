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
public sealed record WorkflowDefinition(
    string Id,
    string Title,
    IReadOnlyList<StageDefinition> Stages,
    long Revision,
    string? Description = null,
    string? Icon = null,
    string? Color = null)
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
}

