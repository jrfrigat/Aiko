namespace Aiko.Application.Contracts;

/// <summary>One bar of an analytics chart: a label, already formatted for the screen, and how much it
/// counted.</summary>
public sealed record AnalyticsBucket(string Label, int Count);

/// <summary>One stage's own traffic over the window: the cards that entered it and the cards that left it,
/// per week, oldest first.</summary>
public sealed record AnalyticsStageFlow(
    string StageId,
    IReadOnlyList<AnalyticsBucket> Entered,
    IReadOnlyList<AnalyticsBucket> Left);

/// <summary>
/// What the project's own history says: how many cards moved per week, which way each stage's traffic ran,
/// and what the board is made of. The daemon reports the traffic it observed rather than deciding what a
/// closure is - which stage ends a pipeline is a fact about the workflow, and workflows live in the project's
/// own files - so the screen that already holds the board reads a closure and a cumulative flow out of this.
/// </summary>
public sealed record ProjectAnalytics(
    IReadOnlyList<AnalyticsBucket> Weekly,
    IReadOnlyList<AnalyticsStageFlow> Flow,
    IReadOnlyList<AnalyticsBucket> ByKind,
    IReadOnlyList<AnalyticsBucket> BySize);

/// <summary>
/// Reads the project's derived numbers. They live in SQLite rather than in files, and deliberately: a stage
/// transition is something the daemon observed, not something the user authored, so losing the table costs a
/// chart and not a card.
/// </summary>
public interface IProjectAnalytics
{
    /// <summary>
    /// Reads the project's charts: transitions per week for the last <paramref name="weeks"/> weeks, each
    /// stage's own traffic per week, counts by kind and counts by size.
    /// </summary>
    ValueTask<ProjectAnalytics> ReadAsync(string projectId, int weeks, CancellationToken cancellationToken);

    /// <summary>
    /// Records that a card entered a stage - the raw material of the throughput chart - with the stage it left,
    /// or null when the card was just created. Called by the card store when a card is created or moved.
    /// </summary>
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
