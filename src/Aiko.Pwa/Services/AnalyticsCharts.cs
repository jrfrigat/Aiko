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
