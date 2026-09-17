using Aiko.Domain.Cards;
using Aiko.Domain.Workflow;

namespace Aiko.Pwa.Contracts;

/// <summary>
/// Request shapes of the PWA. They are top-level types on purpose: the client serializes JSON through
/// <see cref="Services.PwaJsonContext"/>, and a source-generated serializer needs the types to be
/// reachable from the generated code (a private type nested in a component is not).
/// </summary>
internal sealed record CreateCardRequest(
    string CardId,
    CardKind Kind,
    string Title,
    string WorkflowId,
    string StageId,
    decimal OwnPriority,
    IReadOnlyList<string>? DeclaredScopeFiles,
    IReadOnlyDictionary<string, decimal>? CriterionValues,
    string? Size = null);

/// <summary>
/// Moves a card to another workflow stage.
/// </summary>
internal sealed record MoveCardRequest(
    string StageId,
    long ExpectedRevision);

/// <summary>
/// Updates the editable fields of a card.
/// </summary>
/// <param name="Title">New title.</param>
/// <param name="OwnPriority">New own priority, before the size coefficient.</param>
/// <param name="DeclaredScopeFiles">Complete declared scope list.</param>
/// <param name="ExpectedRevision">Revision the caller read.</param>
/// <param name="Size">Size step of the project's grid, or null for none.</param>
/// <param name="CriterionValues">
/// The card's scores per criterion keyed by criterion id, or null to leave the stored values alone - which
/// is what a caller editing only the title wants.
/// </param>
internal sealed record UpdateCardRequest(
    string Title,
    decimal OwnPriority,
    IReadOnlyList<string> DeclaredScopeFiles,
    long ExpectedRevision,
    string? Size = null,
    IReadOnlyDictionary<string, decimal>? CriterionValues = null);

/// <summary>
/// Writes a card artifact. A null version creates or overwrites without a conflict check.
/// </summary>
internal sealed record UpdateArtifactRequest(
    string Path,
    string Content,
    string? ExpectedVersion);

/// <summary>
/// Replaces the stages of a workflow.
/// </summary>
internal sealed record UpdateWorkflowRequest(
    string Title,
    IReadOnlyList<StageDefinition> Stages,
    long ExpectedRevision);

/// <summary>Creates a template by copying one that exists.</summary>
internal sealed record CreateTemplateRequest(string SourceId, string TemplateId, string? Name = null);

/// <summary>Creates a template out of a project, which is how an installation captures a project it likes.</summary>
internal sealed record CreateTemplateFromProjectRequest(string ProjectId, string TemplateId, string? Name = null);

/// <summary>Writes a template's document to a file, so another installation can import it.</summary>
internal sealed record ExportTemplateRequest(string Path);

/// <summary>Reads a template document from a file, under its own id or one given here.</summary>
internal sealed record ImportTemplateRequest(string Path, string? TemplateId = null);

/// <summary>Applies a template to a project that already exists.</summary>
internal sealed record ApplyTemplateRequest(string TemplateId);

/// <summary>
/// Error body of the daemon API.
/// </summary>
internal sealed record ErrorResponse(string Message);

/// <summary>
/// Identification of the running daemon (a superset of the fields the daemon returns).
/// </summary>
internal sealed record SystemInfo(
    string Name,
    string Version,
    string Runtime,
    int ProcessId,
    string BaseUrl,
    DateTimeOffset Time);

/// <summary>
/// A freshly issued one-time pairing code. The browser builds the self-pairing URL from it
/// (<c>#pair=&lt;code&gt;</c>), which is what <c>aiko pair</c> prints for a second machine.
/// </summary>
internal sealed record PairCodeResponse(string Code);
