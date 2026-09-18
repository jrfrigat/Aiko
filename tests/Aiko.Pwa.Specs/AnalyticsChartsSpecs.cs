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
            new(CardKind.Story, 2),
            new(CardKind.Task, 5),
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
}
