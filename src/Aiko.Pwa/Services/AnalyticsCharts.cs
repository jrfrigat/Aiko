using Aiko.Application.Contracts;
using Flare.Components;

namespace Aiko.Pwa.Services;

/// <summary>
/// Builds the charts the project page draws out of the daemon's analytics, so the screen keeps to markup
/// and the mapping from buckets to a data set can be tested without a browser.
/// </summary>
public static class AnalyticsCharts
{
    /// <summary>
    /// A value's bar as a share of the tallest bar in the same chart, in percent. Zero stays zero: a stage
    /// nobody is in, or a size no card carries, must not draw a stub of a bar - a visible sliver reads as
    /// work that is there.
    /// </summary>
    /// <param name="count">The value this bar stands for.</param>
    /// <param name="peak">The largest value of the chart, or zero when it has none.</param>
    public static int SharePercent(int count, int peak)
    {
        if (count <= 0 || peak <= 0)
        {
            return 0;
        }

        return Math.Max(4, (int)Math.Round(count * 100d / peak));
    }

    /// <summary>
    /// The window's tallest bar, which is what every share in the same chart is measured against.
    /// </summary>
    public static int Peak(IReadOnlyList<AnalyticsBucket> buckets) =>
        buckets.Count == 0 ? 0 : buckets.Max(bucket => bucket.Count);

    /// <summary>Stage transitions per week: one bar per week of the window, quiet weeks included.</summary>
    public static ChartData Velocity(IReadOnlyList<AnalyticsBucket> weekly) =>
        new([new ChartSeries("transitions", Values(weekly))], Labels(weekly));

    /// <summary>
    /// The pipeline's two speeds side by side: cards that entered a stage, and cards that reached the end of
    /// their own workflow, per week of the window.
    /// </summary>
    /// <param name="weekly">Stage entries per week, as the daemon counted them.</param>
    /// <param name="closed">Cards that reached the end of their own pipeline, per week.</param>
    public static ChartData Velocity(
        IReadOnlyList<AnalyticsBucket> weekly,
        IReadOnlyList<AnalyticsBucket> closed) =>
        new(
            [
                new ChartSeries("entered", Values(weekly)),
                new ChartSeries("closed", Values(closed)),
            ],
            Labels(weekly));

    /// <summary>
    /// Cards that reached the end of their own pipeline, per week: entries into the last stage of the
    /// workflow a card's own kind belongs to.
    /// </summary>
    /// <remarks>
    /// A transition into a stage the board does not know - one that was renamed or dropped since - is not a
    /// closure, so it is left out rather than counted as one. The window's own labels are followed, so a
    /// quiet week keeps its zero and the series lines up with the entries it is drawn beside.
    /// </remarks>
    /// <param name="closingStageIds">The last stage of every workflow on the board.</param>
    /// <param name="flow">Per-stage flow over the window, as the daemon observed it.</param>
    /// <param name="weeks">The window's week labels, oldest first.</param>
    public static IReadOnlyList<AnalyticsBucket> Closures(
        IReadOnlyCollection<string> closingStageIds,
        IReadOnlyList<AnalyticsStageFlow> flow,
        IReadOnlyList<string> weeks)
    {
        var totals = new int[weeks.Count];
        foreach (var stage in flow)
        {
            if (!closingStageIds.Contains(stage.StageId))
            {
                continue;
            }

            for (var index = 0; index < totals.Length; index++)
            {
                totals[index] += At(stage.Entered, index);
            }
        }

        return [.. weeks.Select((week, index) => new AnalyticsBucket(week, totals[index]))];
    }

    /// <summary>
    /// The cumulative flow diagram: how many cards stood in each stage at the end of every week of the
    /// window, as one series per stage for a stacked chart.
    /// </summary>
    /// <remarks>
    /// The events say what changed; the board says what is true now. The counts are therefore walked
    /// backwards from the board's own snapshot: a stage held one card more last week than this week when a
    /// card entered it, and one fewer when a card left it. A count that comes out negative - the history is
    /// shorter than the window, or the card moved before the daemon observed it - is clamped to zero, since
    /// a stack cannot draw a negative number.
    /// </remarks>
    /// <param name="current">Cards in each stage right now, as the board shows them.</param>
    /// <param name="flow">Per-stage flow over the window, as the daemon observed it.</param>
    /// <param name="weeks">The window's week labels, oldest first.</param>
    public static ChartData CumulativeFlow(
        IReadOnlyList<AnalyticsBucket> current,
        IReadOnlyList<AnalyticsStageFlow> flow,
        IReadOnlyList<string> weeks)
    {
        // No weeks, nothing to stack: a series over no labels is not a diagram.
        if (weeks.Count == 0)
        {
            return new ChartData([], weeks);
        }

        var series = new List<ChartSeries>(current.Count);
        foreach (var stage in current)
        {
            var stageFlow = flow.FirstOrDefault(entry =>
                StringComparer.Ordinal.Equals(entry.StageId, stage.Label));

            // Newest first while walking back - the last week is the board's own count - then reversed.
            var counts = new List<int>(weeks.Count) { stage.Count };
            for (var index = weeks.Count - 1; index > 0; index--)
            {
                counts.Add(Math.Max(
                    0,
                    counts[^1] - At(stageFlow?.Entered, index) + At(stageFlow?.Left, index)));
            }

            counts.Reverse();
            series.Add(new ChartSeries(stage.Label, [.. counts.Select(count => (double)count)]));
        }

        return new ChartData(series, weeks);
    }

    /// <summary>One week of a series, or zero when the series does not reach that far.</summary>
    private static int At(IReadOnlyList<AnalyticsBucket>? buckets, int index) =>
        buckets is not null && index >= 0 && index < buckets.Count ? buckets[index].Count : 0;

    /// <summary>
    /// The board by card type, as a horizontal bar chart: the type's name sits beside its bar, so the
    /// chart says which kind each bar is without a legend.
    /// </summary>
    /// <param name="byKind">Counts by kind, as the daemon grouped them.</param>
    /// <param name="label">Names a kind id the way the screen does.</param>
    public static ChartData ByKind(IReadOnlyList<AnalyticsBucket> byKind, Func<string, string> label) =>
        new([new ChartSeries("cards", Values(byKind))], Labels(byKind, label));

    /// <summary>The board by size step, unsized cards included, on the same kind of horizontal bars.</summary>
    /// <param name="bySize">Counts by size step, as the daemon grouped them.</param>
    /// <param name="label">Names a size step the way the screen does.</param>
    public static ChartData BySize(IReadOnlyList<AnalyticsBucket> bySize, Func<string, string> label) =>
        new([new ChartSeries("cards", Values(bySize))], Labels(bySize, label));

    private static IReadOnlyList<double> Values(IReadOnlyList<AnalyticsBucket> buckets) =>
        buckets.Select(bucket => (double)bucket.Count).ToArray();

    private static IReadOnlyList<string> Labels(IReadOnlyList<AnalyticsBucket> buckets) =>
        buckets.Select(bucket => bucket.Label).ToArray();

    private static IReadOnlyList<string> Labels(
        IReadOnlyList<AnalyticsBucket> buckets,
        Func<string, string> label) =>
        buckets.Select(bucket => label(bucket.Label)).ToArray();
}
