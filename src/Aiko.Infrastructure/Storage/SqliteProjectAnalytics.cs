using System.Globalization;
using Aiko.Application.Contracts;
using Microsoft.Data.Sqlite;

namespace Aiko.Infrastructure.Storage;

/// <summary>
/// The project's derived numbers, read from the SQLite projections.
/// </summary>
/// <remarks>
/// The stage transitions are recorded here rather than in files on purpose: they are what the daemon
/// observed, not what the user authored. A lost table costs a chart; the cards themselves are files and are
/// never touched by this.
/// </remarks>
public sealed class SqliteProjectAnalytics(AikoDatabase database, IProjectCatalog projects) : IProjectAnalytics
{
    /// <inheritdoc />
    public async ValueTask RecordStageAsync(
        string projectId,
        string cardId,
        string? fromStageId,
        string toStageId,
        string kind,
        CancellationToken cancellationToken)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO card_stage_events(project_id, card_id, from_stage_id, to_stage_id, kind, occurred_utc)
            VALUES ($projectId, $cardId, $fromStageId, $toStageId, $kind, $occurredUtc);
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$cardId", cardId);
        command.Parameters.AddWithValue("$fromStageId", (object?)fromStageId ?? DBNull.Value);
        command.Parameters.AddWithValue("$toStageId", toStageId);
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$occurredUtc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<ProjectAnalytics> ReadAsync(
        string projectId,
        int weeks,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        var window = Math.Clamp(weeks, 1, 52);
        // The rows are keyed by the project's immutable id; a caller may hold its readable handle instead.
        var project = await projects.FindAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Unknown Aiko project: {projectId}");
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        // Every week of the window is drawn, including the quiet ones: a chart that hides its empty weeks
        // makes a project look busier than it is.
        var start = StartOfWeek(DateTimeOffset.UtcNow).AddDays(-7 * (window - 1));
        var counts = await ReadWeeklyAsync(connection, project.Id, start, cancellationToken);
        var weekly = new List<AnalyticsBucket>(window);
        for (var index = 0; index < window; index++)
        {
            var week = start.AddDays(7 * index);
            weekly.Add(new AnalyticsBucket(
                week.ToString("dd.MM", CultureInfo.InvariantCulture),
                counts.TryGetValue(week, out var count) ? count : 0));
        }

        return new ProjectAnalytics(
            weekly,
            // Keyed by the resolved id, not by the value the caller sent: the cards projection is written
            // under the project's immutable id, so grouping by the readable handle the project page holds
            // matched nothing and the distribution chart came back empty.
            await ReadGroupedAsync(connection, project.Id, "SELECT kind, COUNT(*) FROM cards WHERE project_id = $projectId GROUP BY kind", cancellationToken),
            await ReadGroupedAsync(
                connection,
                project.Id,
                """
                SELECT COALESCE(json_extract(document_json, '$.size'), '') AS size, COUNT(*)
                FROM cards WHERE project_id = $projectId GROUP BY size ORDER BY size
                """,
                cancellationToken));
    }

    private static async ValueTask<IReadOnlyDictionary<DateTimeOffset, int>> ReadWeeklyAsync(
        SqliteConnection connection,
        string projectId,
        DateTimeOffset start,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT occurred_utc FROM card_stage_events
            WHERE project_id = $projectId AND occurred_utc >= $start
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$start", start.ToString("O", CultureInfo.InvariantCulture));

        var counts = new Dictionary<DateTimeOffset, int>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!DateTimeOffset.TryParse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.None, out var occurred))
            {
                continue;
            }

            var week = StartOfWeek(occurred);
            counts[week] = counts.TryGetValue(week, out var count) ? count + 1 : 1;
        }

        return counts;
    }

    private static async ValueTask<IReadOnlyList<AnalyticsBucket>> ReadGroupedAsync(
        SqliteConnection connection,
        string projectId,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$projectId", projectId);

        var buckets = new List<AnalyticsBucket>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var label = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            buckets.Add(new AnalyticsBucket(label, reader.GetInt32(1)));
        }

        return buckets;
    }

    /// <summary>Midnight of the Monday of the week a moment falls in, in UTC.</summary>
    private static DateTimeOffset StartOfWeek(DateTimeOffset moment)
    {
        var utc = moment.ToUniversalTime();
        var day = new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero);
        return day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
    }
}
