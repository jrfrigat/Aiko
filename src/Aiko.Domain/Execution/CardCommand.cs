using System.Text.Json.Serialization;

namespace Aiko.Domain.Execution;

/// <summary>
/// One command a screen placed for an agent: what to do with which card, who asked, and where it got to.
/// </summary>
/// <remarks>
/// A durable record rather than a live message: the agent is a CLI process that comes and goes, so a request
/// has to wait for it in a file - which is also why the record carries its own timestamps.
/// </remarks>
/// <param name="Id">Identifier of the command, unique within the project.</param>
/// <param name="CardId">
/// Card the command is about, or null for one about the project as a whole - the board pass and a release.
/// Nullable rather than defaulted to a placeholder card, which would show up on that card's screen alone.
/// </param>
/// <param name="Action">What to do.</param>
/// <param name="StageId">Stage to start, for <see cref="CardCommandAction.Start"/>.</param>
/// <param name="ExecutionId">Execution to act on, for the pause, resume and answer actions.</param>
/// <param name="AgentAdapterId">Agent the person asked for, or null when any agent may take it.</param>
/// <param name="Text">Reason for a pause, or the answer itself, depending on the action.</param>
/// <param name="State">Where the command got to.</param>
/// <param name="RequestedBy">Who placed it - a person's name, or the client that did.</param>
/// <param name="CreatedAtUtc">When it was placed.</param>
/// <param name="ClaimedAtUtc">When an agent took it, or null.</param>
/// <param name="FinishedAtUtc">When it was closed - completed, failed or cancelled - or null.</param>
/// <param name="Message">What the agent reported back, or the reason it could not.</param>
public sealed record CardCommand(
    string Id,
    string? CardId,
    CardCommandAction Action,
    string? StageId,
    string? ExecutionId,
    string? AgentAdapterId,
    string? Text,
    CardCommandState State,
    string RequestedBy,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ClaimedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    string? Message)
{
    /// <summary>
    /// True while the command still asks for work: placed, or taken and not yet closed.
    /// </summary>
    /// <remarks>
    /// This, and not a filter a screen remembers to write, is what the queue shows by default: a command
    /// already carried out is history, and history beside "waiting" reads as outstanding work.
    /// </remarks>
    [JsonIgnore]
    public bool IsOpen => State is CardCommandState.Queued or CardCommandState.Taken;
}
