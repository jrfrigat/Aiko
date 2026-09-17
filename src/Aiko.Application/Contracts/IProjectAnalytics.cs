namespace Aiko.Application.Contracts;

/// <summary>
/// One bar of an analytics chart: a label and how much it counted.
/// </summary>
/// <param name="Label">Bucket label, already formatted for the screen.</param>
/// <param name="Count">How many things fell into it.</param>
public sealed record AnalyticsBucket(string Label, int Count);

/// <summary>
/// What the project's own history says: how many cards moved per week, and what the board is made of.
/// </summary>
/// <param name="Weekly">Stage transitions per week, oldest first, one entry per week in the window.</param>
/// <param name="ByKind">Cards by kind.</param>
/// <param name="BySize">Cards by size step, with unsized cards under their own label.</param>
public sealed record ProjectAnalytics(
    IReadOnlyList<AnalyticsBucket> Weekly,
    IReadOnlyList<AnalyticsBucket> ByKind,
    IReadOnlyList<AnalyticsBucket> BySize);

/// <summary>
/// Reads the project's derived numbers.
/// </summary>
/// <remarks>
/// These live in SQLite rather than in files, and deliberately: a stage transition is something the daemon
/// observed, not something the user authored. Losing the table costs a chart, not a card - which is why it is
/// a projection of the work rather than part of it.
/// </remarks>
public interface IProjectAnalytics
{
    /// <summary>
    /// Reads the project's charts: transitions per week for the last <paramref name="weeks"/> weeks, counts
    /// by kind and counts by size.
    /// </summary>
    /// <param name="projectId">Project to read.</param>
    /// <param name="weeks">How many weeks the throughput chart covers.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<ProjectAnalytics> ReadAsync(string projectId, int weeks, CancellationToken cancellationToken);

    /// <summary>
    /// Records that a card entered a stage: the raw material of the throughput chart. Called by the card
    /// store when a card is created or moved.
    /// </summary>
    /// <param name="projectId">Project the card belongs to.</param>
    /// <param name="cardId">Card that moved.</param>
    /// <param name="fromStageId">Stage it left, or null when the card was just created.</param>
    /// <param name="toStageId">Stage it entered.</param>
    /// <param name="kind">Card kind.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask RecordStageAsync(
        string projectId,
        string cardId,
        string? fromStageId,
        string toStageId,
        string kind,
        CancellationToken cancellationToken);
}

/// <summary>
/// What the daemon knows about its own runs: when this process started, how many runs the installation has
/// seen and how many of them ended without a clean shutdown.
/// </summary>
/// <param name="StartedAt">When the current process started.</param>
/// <param name="Uptime">How long it has been running.</param>
/// <param name="Runs">Runs recorded in the database, including the current one.</param>
/// <param name="Crashes">Recorded runs whose shutdown was not clean.</param>
/// <param name="WorkingSetBytes">Resident memory of the current process.</param>
/// <param name="ManagedHeapBytes">Managed heap of the current process, as GC reports it.</param>
public sealed record DaemonTelemetry(
    DateTimeOffset StartedAt,
    TimeSpan Uptime,
    int Runs,
    int Crashes,
    long WorkingSetBytes,
    long ManagedHeapBytes);

/// <summary>
/// Records and reports the daemon's own runs. Installations restart; a screen that says how often, and how
/// often it ended badly, is how an unstable daemon is noticed.
/// </summary>
public interface IDaemonTelemetry
{
    /// <summary>Records the start of this process and returns the telemetry it can report right away.</summary>
    ValueTask<DaemonTelemetry> StartAsync(CancellationToken cancellationToken);

    /// <summary>Marks the current run as cleanly stopped.</summary>
    ValueTask StopAsync(CancellationToken cancellationToken);

    /// <summary>Reads the telemetry of the running process.</summary>
    ValueTask<DaemonTelemetry> ReadAsync(CancellationToken cancellationToken);
}
