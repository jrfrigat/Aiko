using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
using Aiko.Domain.Workflow;

namespace Aiko.Pwa.Contracts;

/// <summary>
/// Request shapes of the PWA. They are top-level types on purpose: the client serializes JSON through
/// <see cref="Services.PwaJsonContext"/>, and a source-generated serializer needs the types to be
/// reachable from the generated code (a private type nested in a component is not).
/// </summary>
/// <summary>Creates a card of any type the project defines.</summary>
/// <param name="CardId">A card id to import, or null to let Aiko name the card.</param>
/// <param name="Kind">Card type id.</param>
/// <param name="Title">Human-readable title.</param>
/// <param name="WorkflowId">Workflow id, or null to take the type's own workflow.</param>
/// <param name="StageId">Must be the backlog stage (or null); a card always starts unelaborated.</param>
/// <param name="OwnPriority">Own priority before the size coefficient.</param>
/// <param name="DeclaredScopeFiles">File patterns the card intends to touch.</param>
/// <param name="CriterionValues">Per-criterion values, or null.</param>
/// <param name="Size">Size step of the project's grid, or null.</param>
/// <param name="Requirements">What the card is asked to do, or null for none.</param>
internal sealed record CreateCardRequest(
    string? CardId,
    string Kind,
    string Title,
    string? WorkflowId,
    string? StageId,
    decimal OwnPriority,
    IReadOnlyList<string>? DeclaredScopeFiles,
    IReadOnlyDictionary<string, decimal>? CriterionValues,
    string? Size = null,
    string? Requirements = null);

/// <summary>
/// Asks an agent to estimate a card. The daemon records who was asked; the person runs the
/// <c>/aiko-estimate</c> command in that agent's terminal.
/// </summary>
internal sealed record EstimateCardRequest(string AgentAdapterId);

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
/// <param name="Requirements">
/// What the card is asked to do, or null to leave the stored text alone. An empty string clears it.
/// </param>
internal sealed record UpdateCardRequest(
    string Title,
    decimal OwnPriority,
    IReadOnlyList<string> DeclaredScopeFiles,
    long ExpectedRevision,
    string? Size = null,
    IReadOnlyDictionary<string, decimal>? CriterionValues = null,
    string? Requirements = null);

/// <summary>
/// Writes a card artifact. A null version creates or overwrites without a conflict check.
/// </summary>
internal sealed record UpdateArtifactRequest(
    string Path,
    string Content,
    string? ExpectedVersion);

/// <summary>
/// Replaces the stages of a workflow, and its own presentation - the card type's name, description,
/// icon and colour.
/// </summary>
internal sealed record UpdateWorkflowRequest(
    string Title,
    IReadOnlyList<StageDefinition> Stages,
    long ExpectedRevision,
    string? Description = null,
    string? Icon = null,
    string? Color = null);

/// <summary>Creates a workflow the project does not have yet: one new card type with its own pipeline.</summary>
internal sealed record CreateWorkflowRequest(
    string Id,
    string Title,
    IReadOnlyList<StageDefinition> Stages,
    string? Description = null,
    string? Icon = null,
    string? Color = null);

/// <summary>Creates a template by copying one that exists.</summary>
internal sealed record CreateTemplateRequest(string SourceId, string TemplateId, string? Name = null);

/// <summary>Creates a template out of a project, which is how an installation captures a project it likes.</summary>
internal sealed record CreateTemplateFromProjectRequest(string ProjectId, string TemplateId, string? Name = null);

/// <summary>
/// Updates what a template says about itself: its name, what it is for, and what an agent must do right after
/// creating a project from it. A field left null keeps the value the template already has.
/// </summary>
internal sealed record UpdateTemplateRequest(
    string? Name = null,
    string? Description = null,
    string? InitializationInstruction = null);

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
    DateTimeOffset Time,
    DaemonTelemetry Telemetry);

/// <summary>
/// A freshly issued one-time pairing code. The browser builds the self-pairing URL from it
/// (<c>#pair=&lt;code&gt;</c>), which is what <c>aiko pair</c> prints for a second machine.
/// </summary>
internal sealed record PairCodeResponse(string Code);
