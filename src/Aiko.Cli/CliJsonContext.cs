using System.Text.Json.Serialization;
using Aiko.Domain.Workflow;

namespace Aiko.Cli;

/// <summary>
/// JSON the CLI has to build itself: a project's stored workflow definition, and the body the daemon's
/// workflow endpoint accepts.
/// </summary>
/// <remarks>
/// Settings are deliberately absent: those documents are read and written as the daemon's own JSON, so the
/// CLI keeps no second copy of their shape.
/// </remarks>
[JsonSerializable(typeof(WorkflowDefinition))]
[JsonSerializable(typeof(StageDefinition))]
[JsonSerializable(typeof(WorkflowUpdate))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    WriteIndented = true)]
internal sealed partial class CliJsonContext : JsonSerializerContext;

/// <summary>
/// The body of <c>PUT /api/v1/projects/{projectId}/workflows/{workflowId}</c>, mirrored field for field.
/// </summary>
/// <remarks>
/// The daemon's own request type is internal to it, so the field names are restated here. This is the one
/// place where the CLI repeats a daemon contract, and it is repeated on purpose: the alternative - guessing a
/// shape at the call site - is how a client comes to send a body the server quietly ignores half of. A rename
/// on either side shows up as the endpoint refusing the document.
/// </remarks>
/// <param name="Title">Display name of the workflow and of the card type it defines.</param>
/// <param name="Stages">The complete stage list, in the order it should be saved in.</param>
/// <param name="ExpectedRevision">Revision the caller read, for the optimistic check.</param>
/// <param name="Description">What the card type is for, or null for none.</param>
/// <param name="Icon">Card type icon id, or null for the default.</param>
/// <param name="Color">Card type colour id, or null for the default.</param>
/// <param name="BlendsWithParent">Whether a card of this type blends its score with its parents'.</param>
internal sealed record WorkflowUpdate(
    string Title,
    IReadOnlyList<StageDefinition> Stages,
    long ExpectedRevision,
    string? Description = null,
    string? Icon = null,
    string? Color = null,
    bool BlendsWithParent = false);
