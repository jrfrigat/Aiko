using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
using Aiko.Domain.Workflow;

namespace Aiko.Pwa.Contracts;

/// <summary>
/// Request shapes of the PWA. They are top-level types on purpose: the client serializes JSON through
/// <see cref="Services.PwaJsonContext"/>, and a source-generated serializer needs the types to be
/// reachable from the generated code (a private type nested in a component is not).
/// </summary>

/// <summary>
/// Connects a set of agents to one project, which is what the add-project form does with the agents the user
/// ticked.
/// </summary>
/// <param name="SelectedAdapterIds">Adapter ids to connect; empty connects nobody.</param>
public sealed record ConnectProjectAgentsRequest(IReadOnlyList<string> SelectedAdapterIds);

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
/// Renames a card type: the workflow's id changes, and with it the folder its cards are filed in and the
/// board section that shows them.
/// </summary>
internal sealed record RenameWorkflowRequest(string NewId);


/// <summary>
/// Moves a card to another workflow stage.
/// </summary>
internal sealed record MoveCardRequest(
    string StageId,
    long ExpectedRevision);

/// <summary>
/// Puts a card into the archive or brings it back to the board.
/// </summary>
/// <remarks>
/// One request for both directions because they are one action on one field, exactly as the daemon serves
/// them: the flag says which way and the revision says which card the caller was looking at.
/// </remarks>
/// <param name="Archived">True to put the card away, false to bring it back.</param>
/// <param name="ExpectedRevision">Revision the caller read the card at.</param>
internal sealed record ArchiveCardRequest(bool Archived, long ExpectedRevision);

/// <summary>
/// Asks the daemon to put every finished card of the project into the archive - the board's own action, which is
/// why the body is optional: sending the list of cards the screen believes are finished would move the decision
/// off the daemon, and the daemon is where the archive gate lives.
/// </summary>
/// <param name="CardIds">
/// Cards to consider, or null for every finished card of the project. The release page narrows the same action
/// to the cards a release carries.
/// </param>
internal sealed record ArchiveFinishedCardsRequest(IReadOnlyList<string>? CardIds = null);

/// <summary>What the archive action came to, as the board's result line reads it.</summary>
/// <param name="Archived">Cards that were put away, by id.</param>
/// <param name="Refused">Cards the archive gate would not accept, each with its reason.</param>
internal sealed record ArchiveFinishedCardsResponse(
    IReadOnlyList<string> Archived,
    IReadOnlyList<ArchiveRefusal> Refused);

/// <summary>One card that stayed on the board, and what the gate said about it.</summary>
/// <param name="CardId">Card that stayed.</param>
/// <param name="Reason">Reason the archive gate gave.</param>
internal sealed record ArchiveRefusal(string CardId, string Reason);

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
/// <param name="Request">
/// The original request, or null to leave the stored text alone. The daemon refuses a change to it once the card
/// has left the backlog, so the form may send it and be told no. The form never sends a reason: the person
/// editing the requirements is the one the note would quote.
/// </param>
internal sealed record UpdateCardRequest(
    string Title,
    decimal OwnPriority,
    IReadOnlyList<string> DeclaredScopeFiles,
    long ExpectedRevision,
    string? Size = null,
    IReadOnlyDictionary<string, decimal>? CriterionValues = null,
    string? Requirements = null,
    string? Request = null);

/// <summary>
/// Links a project to another one, or replaces what the existing link says.
/// </summary>
/// <param name="Description">
/// What the linked project is for, in the words of whoever links it - the sentence an agent reads before it
/// decides that a piece of work belongs to the neighbour.
/// </param>
/// <param name="Reference">Where that project's reference lives, or null: a path or an address.</param>
/// <param name="WhenToUse">When work belongs to that project, or null.</param>
/// <param name="WhenNotToUse">When it belongs here instead, or null.</param>
internal sealed record LinkProjectRequest(
    string Description,
    string? Reference = null,
    string? WhenToUse = null,
    string? WhenNotToUse = null);

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
    string? Color = null,
    bool BlendsWithParent = false);

/// <summary>Creates a workflow the project does not have yet: one new card type with its own pipeline.</summary>
internal sealed record CreateWorkflowRequest(
    string Id,
    string Title,
    IReadOnlyList<StageDefinition> Stages,
    string? Description = null,
    string? Icon = null,
    string? Color = null,
    bool BlendsWithParent = false);

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
    DaemonTelemetry Telemetry,
    string? AssetsVersion = null);

/// <summary>
/// A freshly issued one-time pairing code. The browser builds the self-pairing URL from it
/// (<c>#pair=&lt;code&gt;</c>), which is what <c>aiko pair</c> prints for a second machine.
/// </summary>
internal sealed record PairCodeResponse(string Code);

/// <summary>
/// Starting a stage of a card: the stage to work and the agent that takes it. The daemon decides whether the
/// card may start at all - a card another card blocks is refused, and the refusal is what the page shows.
/// </summary>
internal sealed record StartStageRequest(string StageId, string AgentAdapterId);

/// <summary>Pausing a run, with the reason whoever picks it up will read.</summary>
internal sealed record PauseExecutionRequest(string Reason);

/// <summary>Resuming a run with the agent that continues it.</summary>
internal sealed record ResumeExecutionRequest(string AgentAdapterId);

/// <summary>Handing a run to another agent.</summary>
internal sealed record HandoffExecutionRequest(string TargetAgentAdapterId);

/// <summary>Completing a stage: the files the run reports and the artifacts it produced.</summary>
internal sealed record CompleteStageExecutionRequest(
    IReadOnlyList<string> ActualChangedFiles,
    IReadOnlyList<string> Artifacts);

/// <summary>Cancelling a run. A reason is optional; the daemon records one either way.</summary>
internal sealed record CancelExecutionRequest(string Reason);

/// <summary>
/// Answering a scope request: true accepts the extra files and resumes the run with an agent, false refuses
/// them, which cancels the run because the work cannot be done inside the declared scope.
/// </summary>
internal sealed record ScopeResponseRequest(bool Approved, string? AgentAdapterId);

/// <summary>Approving or rejecting the commit a run is waiting on.</summary>
internal sealed record CommitApprovalRequest(bool Approved);
