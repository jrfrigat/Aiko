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
public sealed record StageRunSummary(string CardId, string StageId, string State);
