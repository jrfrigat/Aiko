using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
using Aiko.Pwa.Services;
using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// Guards the mapping from the daemon's analytics to the project page's charts, and the bar maths the
/// page draws with.
/// </summary>
public sealed class AnalyticsChartsSpecs
{
    [Fact]
    public void A_stage_nobody_is_in_draws_no_bar()
    {
        // The bug this pins: every value got a minimum width, so a stage with no cards still showed a
        // sliver of a bar - which reads as work in progress rather than as an empty stage.
        Assert.Equal(0, AnalyticsCharts.SharePercent(0, 10));
        Assert.Equal(0, AnalyticsCharts.SharePercent(5, 0));
        // A value that is there stays visible even when it is tiny beside the peak.
        Assert.Equal(4, AnalyticsCharts.SharePercent(1, 100));
        Assert.Equal(50, AnalyticsCharts.SharePercent(5, 10));
        Assert.Equal(100, AnalyticsCharts.SharePercent(10, 10));
    }

    [Fact]
    public void The_peak_is_the_tallest_bar_of_the_same_chart()
    {
        Assert.Equal(0, AnalyticsCharts.Peak([]));
        Assert.Equal(
            7,
            AnalyticsCharts.Peak([new AnalyticsBucket("backlog", 3), new AnalyticsBucket("done", 7)]));
    }

    [Fact]
    public void The_velocity_chart_has_one_bar_per_week()
    {
        var weekly = new List<AnalyticsBucket>
        {
            new("01.09", 3),
            new("08.09", 0),
            new("15.09", 7)
        };

        var data = AnalyticsCharts.Velocity(weekly);
        var series = Assert.Single(data.Series);

        // Quiet weeks keep their bar at zero: a chart that hides them makes a project look busier than it is.
        Assert.Equal(new double[] { 3, 0, 7 }, series.Values);
        Assert.Equal(new[] { "01.09", "08.09", "15.09" }, data.Labels!);
    }

    [Fact]
    public void The_distribution_names_its_bars()
    {
        var byKind = new List<AnalyticsBucket>
        {
            new("Story", 2),
            new("Task", 5),
            new("Epic", 1)
        };

        var data = AnalyticsCharts.ByKind(byKind, label => label.ToUpperInvariant());
        var series = Assert.Single(data.Series);

        Assert.Equal(new double[] { 2, 5, 1 }, series.Values);
        // The category labels sit beside the bars, which is what lets the chart do without a legend.
        Assert.Equal(new[] { "STORY", "TASK", "EPIC" }, data.Labels!);
    }

    [Fact]
    public void The_size_chart_labels_an_unsized_card()
    {
        var bySize = new List<AnalyticsBucket> { new(string.Empty, 4), new("M", 1) };

        var data = AnalyticsCharts.BySize(bySize, label => string.IsNullOrEmpty(label) ? "no size" : label);

        Assert.Equal(new double[] { 4, 1 }, Assert.Single(data.Series).Values);
        Assert.Equal(new[] { "no size", "M" }, data.Labels!);
    }

    [Fact]
    public void The_velocity_chart_shows_both_speeds()
    {
        var weekly = new List<AnalyticsBucket> { new("01.09", 3), new("08.09", 0), new("15.09", 7) };
        var closed = new List<AnalyticsBucket> { new("01.09", 1), new("08.09", 0), new("15.09", 4) };

        var data = AnalyticsCharts.Velocity(weekly, closed);

        // Two named series, so the legend says which line is which - the single series this chart used to
        // draw could go without one.
        Assert.Equal("entered", data.Series[0].Label);
        Assert.Equal(new double[] { 3, 0, 7 }, data.Series[0].Values);
        Assert.Equal("closed", data.Series[1].Label);
        Assert.Equal(new double[] { 1, 0, 4 }, data.Series[1].Values);
        Assert.Equal(new[] { "01.09", "08.09", "15.09" }, data.Labels!);
    }

    [Fact]
    public void Only_the_last_stage_of_a_workflow_closes_a_card()
    {
        var weeks = new[] { "01.09", "08.09" };
        var flow = new List<AnalyticsStageFlow>
        {
            new("in-progress", [new("01.09", 5), new("08.09", 2)], [new("01.09", 4), new("08.09", 3)]),
            new("done", [new("01.09", 4), new("08.09", 3)], [new("01.09", 0), new("08.09", 0)]),
        };

        var closed = AnalyticsCharts.Closures(["done"], flow, weeks);

        Assert.Equal(new[] { "01.09", "08.09" }, closed.Select(bucket => bucket.Label));
        // Only the entries into the stage that ends the pipeline count; the traffic of `in-progress` is a
        // move, not a closure.
        Assert.Equal(new[] { 4, 3 }, closed.Select(bucket => bucket.Count));

        // A stage the board no longer knows - renamed or dropped since - is not a closure either.
        Assert.All(AnalyticsCharts.Closures(["shipped"], flow, weeks), bucket => Assert.Equal(0, bucket.Count));
    }

    [Fact]
    public void The_cumulative_flow_is_walked_back_from_the_board()
    {
        var weeks = new[] { "01.09", "08.09", "15.09" };
        // The board today: one card in progress, two done. The events say a card entered `done` in the first
        // week and another in the last, and that one left `in-progress` in between.
        var current = new List<AnalyticsBucket> { new("in-progress", 1), new("done", 2) };
        var flow = new List<AnalyticsStageFlow>
        {
            new(
                "in-progress",
                [new("01.09", 0), new("08.09", 0), new("15.09", 0)],
                [new("01.09", 0), new("08.09", 1), new("15.09", 0)]),
            new(
                "done",
                [new("01.09", 1), new("08.09", 0), new("15.09", 1)],
                [new("01.09", 0), new("08.09", 0), new("15.09", 0)]),
        };

        var data = AnalyticsCharts.CumulativeFlow(current, flow, weeks);

        Assert.Equal(weeks, data.Labels!);
        // The last week is the board's own count, exactly; every earlier week is that count less what
        // entered and plus what left since.
        Assert.Equal(
            new double[] { 1, 1, 2 },
            data.Series.Single(series => series.Label == "done").Values);
        Assert.Equal(
            new double[] { 2, 1, 1 },
            data.Series.Single(series => series.Label == "in-progress").Values);

        // A history shorter than the window cannot invent cards: a count that would come out negative is
        // clamped to zero, because a stack cannot draw a negative number.
        var clamped = AnalyticsCharts.CumulativeFlow(
            [new AnalyticsBucket("done", 0)],
            [new AnalyticsStageFlow("done", [new("01.09", 0), new("08.09", 3)], [])],
            ["01.09", "08.09"]);
        Assert.Equal(new double[] { 0, 0 }, Assert.Single(clamped.Series).Values);
    }

    [Fact]
    public void An_empty_window_draws_no_cumulative_flow()
    {
        var data = AnalyticsCharts.CumulativeFlow([new AnalyticsBucket("done", 2)], [], []);

        Assert.Empty(data.Series);
        Assert.Empty(data.Labels!);
    }
}
