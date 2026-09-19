using Aiko.Domain.Workflow;

namespace Aiko.Server.Contracts;

/// <summary>
/// Workflow update request: new title, description and appearance, the full stage list
/// and the expected revision for optimistic control.
/// </summary>
/// <param name="Title">Display name of the workflow and of the card type it defines.</param>
/// <param name="Stages">The complete stage list, in the order it should be saved in.</param>
/// <param name="ExpectedRevision">Revision the caller read.</param>
/// <param name="Description">What the card type is for, or null for none.</param>
/// <param name="Icon">Card type icon id from the appearance catalog, or null for the default.</param>
/// <param name="Color">Card type colour id from the appearance catalog, or null for the default.</param>
/// <param name="BlendsWithParent">Whether a card of this type blends its score with its parents'.</param>
internal sealed record UpdateWorkflowRequest(
    string Title,
    IReadOnlyList<StageDefinition> Stages,
    long ExpectedRevision,
    string? Description = null,
    string? Icon = null,
    string? Color = null,
    bool BlendsWithParent = false);

/// <summary>
/// Creates a workflow the project does not have yet - one new card type, with its own pipeline.
/// </summary>
/// <param name="Id">Stable ASCII id; also the card type id, for example <c>epic</c>.</param>
/// <param name="Title">Display name of the card type.</param>
/// <param name="Stages">The pipeline stages; the first must be the reserved backlog stage.</param>
/// <param name="Description">What the card type is for, or null for none.</param>
/// <param name="Icon">Card type icon id from the appearance catalog, or null for the default.</param>
/// <param name="Color">Card type colour id from the appearance catalog, or null for the default.</param>
/// <param name="BlendsWithParent">Whether a card of this type blends its score with its parents'.</param>
internal sealed record CreateWorkflowRequest(
    string Id,
    string Title,
    IReadOnlyList<StageDefinition> Stages,
    string? Description = null,
    string? Icon = null,
    string? Color = null,
    bool BlendsWithParent = false);
