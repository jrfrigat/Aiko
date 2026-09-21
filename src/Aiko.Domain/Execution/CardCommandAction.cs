using System.Text.Json.Serialization;

namespace Aiko.Domain.Execution;

/// <summary>
/// What a command asks an agent to do: the delivery half of a stage execution, where the execution itself
/// is already the coordinator's job.
/// </summary>
/// <remarks>
/// A closed set on purpose. Aiko does not run an agent process, so a command can only ask for something an
/// agent already knows how to do in its own run. Four of the actions below map onto the MCP tools one to
/// one; <see cref="RunBoard"/> maps onto a procedure instead (<c>/aiko-run-all</c>), because a pass over the
/// board is a sequence of those same operations and naming it as one action is what keeps a stop in the
/// middle of the pass a stop. An action that no tool and no procedure carries out would be a command nobody
/// can execute, and it does not belong here.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<CardCommandAction>))]
public enum CardCommandAction
{
    /// <summary>Start the named stage of the card.</summary>
    Start,

    /// <summary>Pause the running execution, with the reason in the command's text.</summary>
    Pause,

    /// <summary>Resume a paused, waiting or rate-limited execution.</summary>
    Resume,

    /// <summary>
    /// Answer the question an execution is waiting on: the text is written into the card's discussion and
    /// the execution is woken up, so the answer is not left as a note nobody reads.
    /// </summary>
    Answer,

    /// <summary>
    /// Work the whole board: the cards whose pipeline is unfinished, in board order, each driven to the end
    /// of its own workflow.
    /// </summary>
    /// <remarks>
    /// The one action that names no card. It is a single command rather than one per card on purpose: a pass
    /// stops on the first failure or forbidden action, and a queue of per-card commands would keep going
    /// after that stop - the stop has to be one decision, so the request has to be one record.
    /// </remarks>
    RunBoard,

    /// <summary>
    /// Conduct a release: the second action that names no card, and the second that maps onto a procedure
    /// (<c>/aiko-release</c>) rather than a tool.
    /// </summary>
    /// <remarks>
    /// A release is about the project as a whole, not about one of its cards, so it carries no card id - and
    /// the version the person has in mind travels in the command's text, which the procedure reads before it
    /// chooses one itself. Like the board pass it is a single record: two releases at once would be two tags
    /// for one tree.
    /// </remarks>
    Release
}
