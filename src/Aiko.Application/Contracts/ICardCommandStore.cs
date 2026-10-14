using Aiko.Domain.Execution;

namespace Aiko.Application.Contracts;

/// <summary>
/// What a client asks for when it places a command. A null <c>CardId</c> is a command about the project - a
/// board pass or a release - and <c>Action</c> decides which of the rest are needed: <c>StageId</c> for
/// <c>start</c>, <c>ExecutionId</c> for pause, resume and answer, <c>Text</c> for a pause reason or an answer.
/// </summary>
public sealed record PlaceCommandRequest(
    string? CardId,
    CardCommandAction Action,
    string? StageId = null,
    string? ExecutionId = null,
    string? AgentAdapterId = null,
    string? Text = null,
    string? RequestedBy = null);

/// <summary>
/// The queue of commands a screen placed for an agent: a file beside the project's other documents,
/// <c>.aiko/commands.json</c>, because Aiko cannot start an agent process, so a request from the interface has
/// to survive until the agent next runs. It is readable without the daemon and losing it would lose something a
/// person typed; the store owns the state transitions, which is what stops two agents from carrying out one
/// command twice.
/// </summary>
public interface ICardCommandStore
{
    /// <summary>Reads the project's commands, oldest first; closed ones only when they are asked for.</summary>
    ValueTask<IReadOnlyList<CardCommand>> ListAsync(
        string projectId,
        bool includeClosed,
        CancellationToken cancellationToken);

    /// <summary>Finds one command, or null when there is none.</summary>
    ValueTask<CardCommand?> FindAsync(
        string projectId,
        string commandId,
        CancellationToken cancellationToken);

    /// <summary>Places a command, which starts in the queued state.</summary>
    ValueTask<CardCommand> PlaceAsync(
        string projectId,
        PlaceCommandRequest request,
        CancellationToken cancellationToken);

    /// <summary>Takes a queued command for an agent; a command someone else already took is refused.</summary>
    ValueTask<CardCommand> ClaimAsync(
        string projectId,
        string commandId,
        string agentAdapterId,
        CancellationToken cancellationToken);

    /// <summary>Closes a taken command as completed or failed, with what the agent reported; any other state
    /// is refused.</summary>
    ValueTask<CardCommand> FinishAsync(
        string projectId,
        string commandId,
        CardCommandState state,
        string? message,
        CancellationToken cancellationToken);

    /// <summary>Withdraws a command no agent has taken.</summary>
    ValueTask<CardCommand> CancelAsync(
        string projectId,
        string commandId,
        string? reason,
        CancellationToken cancellationToken);
}

/// <summary>
/// The <c>commands.json</c> document: the command queue of one project. <c>NextSequence</c> is stored rather
/// than derived, so an identifier is never reused after a command is removed - two different commands under
/// one id is how a stale screen would end up describing the wrong piece of work.
/// </summary>
public sealed record CommandDocument(
    int SchemaVersion,
    int NextSequence,
    IReadOnlyList<CardCommand> Entries);
