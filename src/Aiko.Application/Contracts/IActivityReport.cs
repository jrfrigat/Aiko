namespace Aiko.Application.Contracts;

/// <summary>
/// One calendar day of workshop activity: how many recorded things happened on it.
/// </summary>
/// <param name="Date">The day, in UTC.</param>
/// <param name="Count">Executions started plus events published on that day.</param>
public sealed record ActivityDay(DateOnly Date, int Count);

/// <summary>
/// Reads the daemon's own history as a per-day series - the raw material of the dashboard's
/// contribution graph. Aggregating in the store keeps the client from pulling the whole history
/// over the wire just to draw a year of squares.
/// </summary>
public interface IActivityReport
{
    /// <summary>
    /// Returns one entry per day with activity inside the window, oldest first. Days without
    /// activity are omitted; the consumer fills the gaps so the calendar stays rectangular.
    /// </summary>
    /// <param name="days">Window length in days, counted back from today, clamped by the implementation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<IReadOnlyList<ActivityDay>> GetActivityAsync(
        int days,
        CancellationToken cancellationToken);

    /// <summary>
    /// The same series, narrowed to one project: the days that project's own runs and events happened on.
    /// </summary>
    /// <remarks>
    /// A project's own calendar cannot be derived from the installation-wide one - another project's work
    /// would show up in it - so the filter belongs in the store, where the counts are grouped.
    /// </remarks>
    /// <param name="projectId">Project to read, by id or by its readable handle.</param>
    /// <param name="days">Window length in days, counted back from today, clamped by the implementation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<IReadOnlyList<ActivityDay>> GetProjectActivityAsync(
        string projectId,
        int days,
        CancellationToken cancellationToken);
}
