namespace Aiko.Domain.Execution;

/// <summary>
/// The rules a command obeys: what each action has to carry, and when a command may be claimed, closed or
/// withdrawn.
/// </summary>
/// <remarks>
/// The same reasoning as <see cref="Workflow.CardBlocking"/>: these are rules about the pipeline, not about
/// one caller, so they sit where every caller - the REST endpoint, the MCP tool, the CLI - reads them from
/// one place instead of each remembering them. What a caller then does with a refusal stays its own decision.
/// </remarks>
public static class CardCommands
{
    /// <summary>
    /// The action a wire value names, or a refusal when it names none.
    /// </summary>
    /// <param name="value">Action as a client sent it, for example <c>start</c>.</param>
    public static CardCommandAction ParseAction(string value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "start" => CardCommandAction.Start,
            "pause" => CardCommandAction.Pause,
            "resume" => CardCommandAction.Resume,
            "answer" => CardCommandAction.Answer,
            _ => throw new ArgumentException($"Unknown command action: {value}", nameof(value))
        };

    /// <summary>
    /// The state a wire value names, or a refusal when it names none.
    /// </summary>
    /// <param name="value">State as a client sent it, for example <c>completed</c>.</param>
    public static CardCommandState ParseState(string value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "queued" => CardCommandState.Queued,
            "taken" => CardCommandState.Taken,
            "completed" => CardCommandState.Completed,
            "failed" => CardCommandState.Failed,
            "cancelled" => CardCommandState.Cancelled,
            _ => throw new ArgumentException($"Unknown command state: {value}", nameof(value))
        };

    /// <summary>
    /// Rejects the commands that name nothing an agent could act on, in either direction: a card action
    /// without its card, an action that names no execution, or a pass over the board that names a card.
    /// </summary>
    /// <remarks>
    /// The board pass is refused a card, a stage and an execution rather than having them quietly ignored:
    /// a field nobody reads is how a request and its meaning drift apart, and the next reader would believe
    /// the command said something it did not.
    /// </remarks>
    /// <param name="action">Action the command asks for.</param>
    /// <param name="cardId">Card the command is about, or null for the board pass.</param>
    /// <param name="stageId">Stage to start, when the action needs one.</param>
    /// <param name="executionId">Execution to act on, when the action needs one.</param>
    /// <param name="text">Text the action needs, when it needs one.</param>
    /// <exception cref="ArgumentException">The action is missing something it cannot do without.</exception>
    public static void Validate(
        CardCommandAction action,
        string? cardId,
        string? stageId,
        string? executionId,
        string? text)
    {
        if (action is CardCommandAction.RunBoard)
        {
            if (!string.IsNullOrWhiteSpace(cardId) ||
                !string.IsNullOrWhiteSpace(stageId) ||
                !string.IsNullOrWhiteSpace(executionId))
            {
                throw new ArgumentException(
                    "Working the board is not about one card or one run, so it names neither.",
                    nameof(cardId));
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(cardId))
        {
            throw new ArgumentException(
                $"The '{action}' action needs the card it is about.",
                nameof(cardId));
        }

        if (action is CardCommandAction.Start)
        {
            if (string.IsNullOrWhiteSpace(stageId))
            {
                throw new ArgumentException("Starting a stage needs the stage to start.", nameof(stageId));
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(executionId))
        {
            throw new ArgumentException(
                $"The '{action}' action needs the execution to act on.",
                nameof(executionId));
        }

        if (action is CardCommandAction.Pause or CardCommandAction.Answer &&
            string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException(
                action is CardCommandAction.Pause
                    ? "Pausing an execution needs the reason, which is what whoever resumes it reads."
                    : "Answering a question needs the answer itself.",
                nameof(text));
        }
    }

    /// <summary>
    /// Why the command may not be placed at all, or null when it may be.
    /// </summary>
    /// <param name="action">Action the command asks for.</param>
    /// <param name="existing">The project's commands, from which the open ones are read.</param>
    /// <remarks>
    /// One project works its board once. Two open passes would take cards in the same order and fight over
    /// them: the second agent would be refused by the concurrency gate on one card and start the next, so
    /// the two passes would interleave and neither stop would mean anything. Unlike the claim rule, this one
    /// is about placing - the person pressing the button twice is the mistake being caught.
    /// </remarks>
    public static string? RefusePlace(
        CardCommandAction action,
        IReadOnlyList<CardCommand> existing)
    {
        ArgumentNullException.ThrowIfNull(existing);
        if (action is not CardCommandAction.RunBoard)
        {
            return null;
        }

        return existing.FirstOrDefault(command =>
            command.Action is CardCommandAction.RunBoard && command.IsOpen) is { } open
            ? $"the board is already being worked: command '{open.Id}' is {open.State}. "
                + "Let that pass finish, or withdraw it first."
            : null;
    }

    /// <summary>
    /// Why the command may not be taken by <paramref name="agentAdapterId"/>, or null when it may be.
    /// </summary>
    /// <param name="command">Command an agent wants to take.</param>
    /// <param name="agentAdapterId">Agent that wants to take it.</param>
    /// <remarks>
    /// This is the guard that keeps two agents from carrying out one command twice. Whoever loses the race is
    /// told what happened instead of silently working the same stage again. The second half of the rule is
    /// the person's own instruction: a command that named an agent is for that agent, and another one taking
    /// it would be the queue overruling the person who placed the command.
    /// </remarks>
    public static string? RefuseClaim(CardCommand command, string agentAdapterId)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentAdapterId);

        if (command.State is not CardCommandState.Queued)
        {
            return $"command '{command.Id}' is {command.State} and cannot be taken" +
                   (command.ClaimedAtUtc is { } claimedAt
                       ? $" - an agent took it at {claimedAt:u}"
                       : string.Empty) + ".";
        }

        return command.AgentAdapterId is { Length: > 0 } asked &&
               !StringComparer.OrdinalIgnoreCase.Equals(asked, agentAdapterId.Trim())
            ? $"command '{command.Id}' was placed for agent '{asked}', so this run leaves it for that agent."
            : null;
    }

    /// <summary>
    /// Why the command may not be closed, or null when it may be.
    /// </summary>
    /// <param name="command">Command an agent wants to close.</param>
    public static string? RefuseFinish(CardCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command.State is CardCommandState.Taken
            ? null
            : $"command '{command.Id}' is {command.State}: only a command an agent has taken can be " +
              "closed, so take it first.";
    }

    /// <summary>
    /// Why the command may not be withdrawn, or null when it may be.
    /// </summary>
    /// <param name="command">Command a person wants to withdraw.</param>
    /// <remarks>
    /// Only a command nobody has taken can be withdrawn. One an agent already holds is closed by that agent,
    /// because a person cannot know whether the work it asked for has happened - and a command that says
    /// "cancelled" while its stage is being worked is worse than one that says "taken".
    /// </remarks>
    public static string? RefuseCancel(CardCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command.State is CardCommandState.Queued
            ? null
            : $"command '{command.Id}' is {command.State} and cannot be withdrawn" +
              (command.State is CardCommandState.Taken
                  ? " - an agent has taken it and closes it itself."
                  : ".") ;
    }
}
