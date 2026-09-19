using System.Text.Json.Serialization;

namespace Aiko.Domain.Execution;

/// <summary>
/// What a command asks an agent to do: the delivery half of a stage execution, where the execution itself
/// is already the coordinator's job.
/// </summary>
/// <remarks>
/// A closed set on purpose. Aiko does not run an agent process, so a command can only ask for something an
/// agent already knows how to do in its own run - and the four below are exactly the operations the MCP
/// tools offer. A fifth action that no tool can carry out would be a command nobody can execute.
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
    Answer
}
