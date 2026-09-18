using System.Globalization;
using Aiko.Application.Contracts;
using Aiko.Infrastructure.Storage;

namespace Aiko.Infrastructure.Execution;

/// <summary>
/// SQLite-backed activity series. Two things mark a day as active: a stage execution was started
/// (<c>executions.created_utc</c>, every attempt to run an agent) or the daemon published an event
/// (<c>events.occurred_utc</c>, every card, relation and execution change the UI was told about).
/// The counts are grouped in the database, so a year of history costs one query and a few hundred
/// rows. The same query answers for the whole installation and for one project, with the filter
/// applied where the grouping happens.
/// </summary>
public sealed class SqliteActivityReport(AikoDatabase database, IProjectCatalog projects) : IActivityReport
{
    /// <summary>Longest window the report will serve: a year and a day, i.e. a leap year of squares.</summary>
    public const int MaxDays = 366;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ActivityDay>> GetActivityAsync(
        int days,
        CancellationToken cancellationToken) =>
        ReadAsync(projectId: null, days, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ActivityDay>> GetProjectActivityAsync(
        string projectId,
        int days,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        // The rows are keyed by the project's immutable id; the project page holds the readable handle.
        var project = await projects.FindAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Unknown Aiko project: {projectId}");
        return await ReadAsync(project.Id, days, cancellationToken);
    }

    private async ValueTask<IReadOnlyList<ActivityDay>> ReadAsync(
        string? projectId,
        int days,
        CancellationToken cancellationToken)
    {
        var window = Math.Clamp(days, 1, MaxDays);
        // The stored timestamps are ISO-8601 UTC strings, so their ten-character date prefix is
        // directly comparable: SQLite orders them as text and a 'yyyy-MM-dd' bound is a valid floor.
        var since = DateTime.UtcNow.Date
            .AddDays(-(window - 1))
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // One statement answers both questions: a null project id leaves every row in, a set one keeps
        // the project's own.
        command.CommandText =
            """
            SELECT day, SUM(day_count) AS total
            FROM (
                SELECT substr(created_utc, 1, 10) AS day, COUNT(*) AS day_count
                FROM executions
                WHERE created_utc >= $since
                  AND ($projectId IS NULL OR project_id = $projectId)
                GROUP BY day
                UNION ALL
                SELECT substr(occurred_utc, 1, 10) AS day, COUNT(*) AS day_count
                FROM events
                WHERE occurred_utc >= $since
                  AND ($projectId IS NULL OR project_id = $projectId)
                GROUP BY day
            )
            GROUP BY day
            ORDER BY day;
            """;
        command.Parameters.AddWithValue("$since", since);
        command.Parameters.AddWithValue("$projectId", (object?)projectId ?? DBNull.Value);

        var result = new List<ActivityDay>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (DateOnly.TryParseExact(
                    reader.GetString(0),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date))
            {
                result.Add(new ActivityDay(date, reader.GetInt32(1)));
            }
        }

        return result;
    }
}
