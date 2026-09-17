using Microsoft.Data.Sqlite;
using Aiko.Application.Contracts;
using Aiko.Infrastructure.Storage;

namespace Aiko.Infrastructure.Projects;

/// <summary>
/// Project catalog backed by the projects table of the SQLite database.
/// </summary>
/// <remarks>
/// The table carries two identifiers per project: the generated id, which is the immutable key every card
/// file, relation and agent MCP endpoint points at, and the slug, which is the readable handle the UI links
/// with. Lookups accept either of them, the id first.
/// </remarks>
public sealed class SqliteProjectCatalog(AikoDatabase database) : IProjectCatalog
{
    /// <summary>The columns every read selects, in the order <see cref="ReadProject"/> expects them.</summary>
    private const string SelectColumns = "SELECT id, name, root_path, slug FROM projects";

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<RegisteredProject>> ListAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"{SelectColumns} ORDER BY name, id;";

        var projects = new List<RegisteredProject>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            projects.Add(ReadProject(reader));
        }

        return projects;
    }

    /// <inheritdoc />
    public async ValueTask<RegisteredProject?> FindAsync(
        string projectIdOrSlug,
        CancellationToken cancellationToken)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        // One statement answers both questions, and the id wins when a value could be either: a project
        // named "01a0b037..." must not steal the lookup of the project whose id that actually is.
        command.CommandText =
            $"{SelectColumns} WHERE id = $value OR slug = $value " +
            "ORDER BY CASE WHEN id = $value THEN 0 ELSE 1 END LIMIT 1;";
        command.Parameters.AddWithValue("$value", projectIdOrSlug);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadProject(reader) : null;
    }

    /// <inheritdoc />
    public async ValueTask<bool> IsSlugTakenAsync(
        string slug,
        string? exceptProjectId,
        CancellationToken cancellationToken)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM projects WHERE slug = $slug AND ($except IS NULL OR id <> $except);";
        command.Parameters.AddWithValue("$slug", slug);
        command.Parameters.AddWithValue("$except", (object?)exceptProjectId ?? DBNull.Value);

        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L) > 0;
    }

    /// <inheritdoc />
    public async ValueTask SaveAsync(
        RegisteredProject project,
        CancellationToken cancellationToken)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO projects(id, name, root_path, slug, updated_utc)
            VALUES ($id, $name, $rootPath, $slug, $updatedUtc)
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name,
                root_path = excluded.root_path,
                slug = excluded.slug,
                updated_utc = excluded.updated_utc;
            """;
        command.Parameters.AddWithValue("$id", project.Id);
        command.Parameters.AddWithValue("$name", project.Name);
        command.Parameters.AddWithValue("$rootPath", project.RootPath);
        command.Parameters.AddWithValue("$slug", (object?)project.Slug ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedUtc", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<bool> RemoveAsync(string projectId, CancellationToken cancellationToken)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        // One statement clears the project's projections too: cards, executions and events all declare
        // ON DELETE CASCADE against projects(id). Nothing on disk is touched.
        command.CommandText = "DELETE FROM projects WHERE id = $id OR slug = $id;";
        command.Parameters.AddWithValue("$id", projectId);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private static RegisteredProject ReadProject(SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
}

