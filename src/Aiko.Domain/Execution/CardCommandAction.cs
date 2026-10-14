using System.Text.Json.Serialization;

namespace Aiko.Domain.Execution;

/// <summary>
/// What a command asks an agent to do: the delivery half of a stage execution, where the execution itself
/// is already the coordinator's job.
/// </summary>
/// <remarks>
/// A closed set: Aiko runs no agent process, so an action has to be something an agent can carry out in its
/// own run - a tool, or a procedure named beside the action. An action nothing carries out would be a
/// command nobody can execute.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<CardCommandAction>))]
public enum CardCommandAction
{
    Start,

    Pause,

    Resume,

    /// <summary>
    /// Answer the question an execution waits on: the text goes into the card's discussion and the execution
    /// is woken up, so the answer is not left as a note nobody reads.
    /// </summary>
    Answer,

    /// <summary>
    /// Work the whole board: the cards whose pipeline is unfinished, in board order, each driven to the end
    /// of its own workflow.
    /// </summary>
    /// <remarks>
    /// One command rather than one per card, because a pass stops on the first failure: the stop is one
    /// decision, so the request is one record.
    /// </remarks>
    RunBoard,

    /// <summary>
    /// Conduct a release: the action that names no card and carries the scheme in the command's text.
    /// </summary>
    /// <remarks>
    /// A release is about the project as a whole, so it carries no card id, and the scheme in the text is
    /// what says which order to work by. One record, because two releases at once would be two tags for one
    /// tree.
    /// </remarks>
    Release
}
