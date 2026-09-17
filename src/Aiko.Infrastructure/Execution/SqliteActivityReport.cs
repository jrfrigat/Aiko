using System.Globalization;
using Aiko.Application.Contracts;
using Aiko.Infrastructure.Storage;

namespace Aiko.Infrastructure.Execution;

/// <summary>
/// SQLite-backed activity series. Two things mark a day as active: a stage execution was started
/// (<c>executions.created_utc</c>, every attempt to run an agent) or the daemon published an event
/// (<c>events.occurred_utc</c>, every card, relation and execution change the UI was told about).
/// The counts are grouped in the database, so a year of history costs one query and a few hundred
/// rows.
/// </summary>
public sealed class SqliteActivityReport(AikoDatabase database) : IActivityReport
{
    /// <summary>Longest window the report will serve: a year and a day, i.e. a leap year of squares.</summary>
    public const int MaxDays = 366;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ActivityDay>> GetActivityAsync(
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
        command.CommandText =
            """
            SELECT day, SUM(day_count) AS total
            FROM (
                SELECT substr(created_utc, 1, 10) AS day, COUNT(*) AS day_count
                FROM executions
                WHERE created_utc >= $since
                GROUP BY day
                UNION ALL
                SELECT substr(occurred_utc, 1, 10) AS day, COUNT(*) AS day_count
                FROM events
                WHERE occurred_utc >= $since
                GROUP BY day
            )
            GROUP BY day
            ORDER BY day;
            """;
        command.Parameters.AddWithValue("$since", since);

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
