namespace Aiko.Application.Contracts;

/// <summary>
/// One calendar day of workshop activity: how many recorded things happened on it. <c>Count</c> is executions
/// started plus events published.
/// </summary>
public sealed record ActivityDay(DateOnly Date, int Count);

/// <summary>Reads the daemon's own history as a per-day series, which the dashboard's contribution graph is
/// drawn from. Aggregating in the store keeps the client from pulling the whole history over the wire.</summary>
public interface IActivityReport
{
    /// <summary>Returns one entry per day with activity inside the window, oldest first; days without activity
    /// are omitted, and the consumer fills the gaps so the calendar stays rectangular. The window is counted
    /// back from today and clamped by the implementation.</summary>
    ValueTask<IReadOnlyList<ActivityDay>> GetActivityAsync(
        int days,
        CancellationToken cancellationToken);

    /// <summary>
    /// The same series, narrowed to one project: the days that project's own runs and events happened on. A
    /// project's own calendar cannot be derived from the installation-wide one - another project's work would
    /// show up in it - so the filter belongs in the store, where the counts are grouped.
    /// </summary>
    ValueTask<IReadOnlyList<ActivityDay>> GetProjectActivityAsync(
        string projectId,
        int days,
        CancellationToken cancellationToken);
}
