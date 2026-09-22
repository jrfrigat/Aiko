using System.Text.Json.Serialization;
using Aiko.Domain.Execution;

namespace Aiko.Application.Contracts;

/// <summary>
/// Where one stage of one card got to: the latest run of that pair, as the board carries it.
/// </summary>
/// <remarks>
/// Facts rather than a verdict. Everywhere else in Aiko a stage's state is derived from its runs, so the board
/// sends what the runs say and the screen decides what it means - a second, stored copy of "where is this card"
/// is the thing that would sooner or later disagree with the runs tab beside it. One row per (card, stage) pair
/// keeps a project with a long history from shipping its whole journal to draw a few badges.
/// </remarks>
/// <param name="CardId">Card the run belongs to.</param>
/// <param name="StageId">Stage the run belongs to.</param>
/// <param name="State">
/// State of that pair's latest run, as <c>StageExecutionState</c> writes it ("Running", "Paused", ...).
/// </param>
public sealed record StageRunSummary(string CardId, string StageId, string State)
{
    /// <summary>
    /// <see cref="State"/> as the stage-execution state it names, or null when the text names none.
    /// </summary>
    /// <remarks>
    /// The state travels as text because that is what the runs table stores, while the rules that judge a card
    /// work with <c>StageExecutionState</c>. Parsing it here keeps every caller from writing the same
    /// <c>Enum.TryParse</c>, and a text nobody recognises reads as "nothing known" rather than as whatever it
    /// happens to parse to - which is why the value is checked against the enum and not only parsed.
    /// </remarks>
    [JsonIgnore]
    public StageExecutionState? StateValue =>
        Enum.TryParse<StageExecutionState>(State, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : null;
}
