using Aiko.Domain.Execution;

namespace Aiko.Application.Contracts;

/// <summary>
/// What a client asks for when it places a command.
/// </summary>
/// <param name="CardId">Card the command is about.</param>
/// <param name="Action">What to do; see <see cref="CardCommands.Validate"/> for what each one requires.</param>
/// <param name="StageId">Stage to start, for the <c>start</c> action.</param>
/// <param name="ExecutionId">Execution to act on, for the other three actions.</param>
/// <param name="AgentAdapterId">Agent the person asked for, or null when any agent may take it.</param>
/// <param name="Text">Reason for a pause, or the answer itself.</param>
/// <param name="RequestedBy">Who placed it, or null for the local user.</param>
public sealed record PlaceCommandRequest(
    string CardId,
    CardCommandAction Action,
    string? StageId = null,
    string? ExecutionId = null,
    string? AgentAdapterId = null,
    string? Text = null,
    string? RequestedBy = null);

/// <summary>
/// The queue of commands a screen placed for an agent.
/// </summary>
/// <remarks>
/// Aiko cannot start an agent process, so a request from the interface has to survive until the agent next
/// runs. It lives as a file beside the project's other documents - <c>.aiko/commands.json</c> - for the same
/// reason a card's notes do: it has to be readable without the daemon, and losing it would lose something a
/// person typed. The store owns the state transitions, because they are what stops two agents from carrying
/// out one command twice.
/// </remarks>
public interface ICardCommandStore
{
    /// <summary>
    /// Reads the project's commands, oldest first.
    /// </summary>
    /// <param name="projectId">Project to read, by id or by its readable handle.</param>
    /// <param name="includeClosed">
    /// True to include completed, failed and cancelled commands; false for the open queue only.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<IReadOnlyList<CardCommand>> ListAsync(
        string projectId,
        bool includeClosed,
        CancellationToken cancellationToken);

    /// <summary>
    /// Finds one command or returns null.
    /// </summary>
    /// <param name="projectId">Project to read.</param>
    /// <param name="commandId">Command to find.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<CardCommand?> FindAsync(
        string projectId,
        string commandId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Places a command, which starts in the queued state.
    /// </summary>
    /// <param name="projectId">Project to write to.</param>
    /// <param name="request">What the client asked for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<CardCommand> PlaceAsync(
        string projectId,
        PlaceCommandRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Takes a queued command for an agent; a command someone else already took is refused.
    /// </summary>
    /// <param name="projectId">Project holding the command.</param>
    /// <param name="commandId">Command to take.</param>
    /// <param name="agentAdapterId">Agent taking it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<CardCommand> ClaimAsync(
        string projectId,
        string commandId,
        string agentAdapterId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Closes a taken command as completed or failed, with what the agent reported.
    /// </summary>
    /// <param name="projectId">Project holding the command.</param>
    /// <param name="commandId">Command to close.</param>
    /// <param name="state">Completed or failed; any other state is refused.</param>
    /// <param name="message">What the agent reported, or the reason it failed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<CardCommand> FinishAsync(
        string projectId,
        string commandId,
        CardCommandState state,
        string? message,
        CancellationToken cancellationToken);

    /// <summary>
    /// Withdraws a command no agent has taken.
    /// </summary>
    /// <param name="projectId">Project holding the command.</param>
    /// <param name="commandId">Command to withdraw.</param>
    /// <param name="reason">Why it was withdrawn, or null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<CardCommand> CancelAsync(
        string projectId,
        string commandId,
        string? reason,
        CancellationToken cancellationToken);
}

/// <summary>
/// The <c>commands.json</c> document: the command queue of one project.
/// </summary>
/// <param name="SchemaVersion">Schema version of the file.</param>
/// <param name="NextSequence">
/// The number the next command's identifier takes. It is stored rather than derived so an identifier is
/// never reused after a command is removed - two different commands under one id is how a stale screen
/// would end up describing the wrong piece of work.
/// </param>
/// <param name="Entries">The commands, oldest first.</param>
public sealed record CommandDocument(
    int SchemaVersion,
    int NextSequence,
    IReadOnlyList<CardCommand> Entries);
