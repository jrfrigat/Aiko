using Aiko.Application.Contracts;
using Aiko.Pwa.Services;
using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// Guards the contribution calendar the dashboard and a project's page both draw.
/// </summary>
public sealed class ActivityHeatmapSpecs
{
    private static readonly DateOnly Today = new(2026, 9, 18);

    [Fact]
    public void The_calendar_is_whole_weeks_ending_today()
    {
        var cells = ActivityHeatmap.Build([], Today);

        // Monday first, so every column of the grid is a calendar week. The rectangle ends on today, so the
        // last column is only as full as the week is - the count is a multiple of seven only on a Sunday.
        Assert.Equal(DayOfWeek.Monday, cells[0].Date.DayOfWeek);
        Assert.Equal(Today, cells[^1].Date);
        Assert.Equal((Today.DayNumber - cells[0].Date.DayNumber) + 1, cells.Count);
        Assert.True(cells.Count >= ActivityHeatmap.WindowDays);
        Assert.All(cells, cell => Assert.True(cell.Date <= Today));
        // The daemon omits quiet days; the calendar draws them, at zero.
        Assert.All(cells, cell => Assert.Equal(0, cell.Count));
        Assert.All(cells, cell => Assert.Equal(0, cell.Level));
    }

    [Fact]
    public void A_day_is_shaded_by_how_busy_it_was()
    {
        var days = new List<ActivityDay>
        {
            new(Today, 40),
            new(Today.AddDays(-1), 20),
            new(Today.AddDays(-2), 10)
        };

        var byDate = ActivityHeatmap.Build(days, Today).ToDictionary(cell => cell.Date);

        Assert.Equal(4, byDate[Today].Level);
        Assert.Equal(2, byDate[Today.AddDays(-1)].Level);
        Assert.Equal(1, byDate[Today.AddDays(-2)].Level);
        Assert.Equal(0, byDate[Today.AddDays(-3)].Level);
        Assert.Equal(70, ActivityHeatmap.Total(days));
        Assert.Equal(40, ActivityHeatmap.TotalOn(days, Today));
        // A day the series does not mention is a day with nothing on it, not an error.
        Assert.Equal(0, ActivityHeatmap.TotalOn(days, Today.AddDays(-9)));
    }

    [Fact]
    public void A_window_of_quiet_days_is_shaded_by_its_own_counts()
    {
        // A window whose busiest day is a handful is shaded by the raw count: four quiet days must not all
        // read as the darkest shade there is.
        Assert.Equal(1, ActivityHeatmap.Level(1, 4));
        Assert.Equal(4, ActivityHeatmap.Level(4, 4));
        Assert.Equal(0, ActivityHeatmap.Level(0, 4));
        Assert.Equal(0, ActivityHeatmap.Level(3, 0));
    }
}
