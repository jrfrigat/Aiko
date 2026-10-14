using System.Text.Json.Serialization;
using Aiko.Domain.Execution;

namespace Aiko.Application.Contracts;

/// <summary>
/// Where one stage of one card got to: the latest run of that pair, as the board carries it. Facts rather than
/// a verdict - everywhere else a stage's state is derived from its runs, so the board sends what the runs say
/// and the screen decides what it means - and one row per (card, stage) pair keeps a project with a long
/// history from shipping its whole journal to draw a few badges.
/// </summary>
public sealed record StageRunSummary(string CardId, string StageId, string State)
{
    /// <summary>
    /// <c>State</c> as the stage-execution state it names, or null when the text names none. It travels as text
    /// because that is what the runs table stores, while the rules that judge a card work with
    /// <c>StageExecutionState</c>; a text nobody recognises reads as "nothing known" rather than as whatever it
    /// happens to parse to.
    /// </summary>
    [JsonIgnore]
    public StageExecutionState? StateValue =>
        Enum.TryParse<StageExecutionState>(State, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : null;
}
