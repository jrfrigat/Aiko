using Aiko.Application.Contracts;

namespace Aiko.Pwa.Services;

/// <summary>
/// One day of a contribution calendar, with the shade it is drawn in.
/// </summary>
/// <param name="Date">The day, in UTC.</param>
/// <param name="Count">How much happened on it.</param>
/// <param name="Level">Shade, 0 for an idle day and 1..4 growing with the day.</param>
public sealed record HeatCell(DateOnly Date, int Count, int Level);

/// <summary>
/// Turns the sparse per-day series the daemon serves into the rectangle a calendar draws: whole weeks,
/// Monday first, one cell per day up to today.
/// </summary>
/// <remarks>
/// Shared by the installation-wide dashboard and a project's own page, so both calendars shade the same
/// day the same way. The daemon omits quiet days to keep the payload small, so filling the gaps - and
/// knowing which week a day belongs to - is the client's job, and it lives here rather than in either
/// screen.
/// </remarks>
public static class ActivityHeatmap
{
    /// <summary>A year of squares, aligned to whole weeks.</summary>
    public const int WindowDays = 364;

    /// <summary>The calendar's cells, oldest first, covering the window that ends on <paramref name="today"/>.</summary>
    /// <param name="days">The days the daemon counted; quiet days may be missing.</param>
    /// <param name="today">The last day of the calendar, which is what "today" means to the caller.</param>
    /// <param name="windowDays">How far back the calendar reaches.</param>
    public static IReadOnlyList<HeatCell> Build(
        IReadOnlyList<ActivityDay> days,
        DateOnly today,
        int windowDays = WindowDays)
    {
        ArgumentNullException.ThrowIfNull(days);
        var counts = new Dictionary<DateOnly, int>();
        foreach (var day in days)
        {
            counts[day.Date] = day.Count;
        }

        var peak = counts.Count == 0 ? 0 : counts.Values.Max();
        // Every column is a calendar week, so the rectangle starts on the Monday of the first week in
        // the window rather than on the first day the window happens to include.
        var start = today.AddDays(-(Math.Max(windowDays, 1) - 1));
        start = start.AddDays(-(((int)start.DayOfWeek + 6) % 7));

        var cells = new List<HeatCell>();
        for (var date = start; date <= today; date = date.AddDays(1))
        {
            var count = counts.TryGetValue(date, out var value) ? value : 0;
            cells.Add(new HeatCell(date, count, Level(count, peak)));
        }

        return cells;
    }

    /// <summary>Everything recorded inside the series: the figure the empty state is decided on.</summary>
    public static int Total(IReadOnlyList<ActivityDay> days) =>
        days.Sum(day => day.Count);

    /// <summary>What happened on one day, or zero when the series has no entry for it.</summary>
    public static int TotalOn(IReadOnlyList<ActivityDay> days, DateOnly day) =>
        days.Where(item => item.Date == day).Sum(item => item.Count);

    /// <summary>
    /// The cell's shade: 0 is an idle day, 1..4 grow with the day relative to the busiest one. A window
    /// whose busiest day is tiny is shaded by the raw count, so four quiet days do not all read as the
    /// darkest there is.
    /// </summary>
    public static int Level(int count, int peak)
    {
        if (count <= 0 || peak <= 0)
        {
            return 0;
        }

        return peak <= 4
            ? Math.Min(count, 4)
            : Math.Clamp((int)Math.Ceiling(count * 4d / peak), 1, 4);
    }
}
