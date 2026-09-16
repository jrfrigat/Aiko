using Microsoft.Data.Sqlite;
using Aiko.Application.Contracts;
using Aiko.Infrastructure.Storage;

namespace Aiko.Infrastructure.Projects;

/// <summary>
/// Project catalog backed by the projects table of the SQLite database.
/// </summary>
public sealed class SqliteProjectCatalog(AikoDatabase database) : IProjectCatalog
{
    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<RegisteredProject>> ListAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, root_path FROM projects ORDER BY name, id;";

        var projects = new List<RegisteredProject>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            projects.Add(new RegisteredProject(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2)));
        }

        return projects;
    }

    /// <inheritdoc />
    public async ValueTask<RegisteredProject?> FindAsync(
        string projectId,
        CancellationToken cancellationToken)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, root_path FROM projects WHERE id = $id;";
        command.Parameters.AddWithValue("$id", projectId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new RegisteredProject(reader.GetString(0), reader.GetString(1), reader.GetString(2))
            : null;
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
            INSERT INTO projects(id, name, root_path, updated_utc)
            VALUES ($id, $name, $rootPath, $updatedUtc)
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name,
                root_path = excluded.root_path,
                updated_utc = excluded.updated_utc;
            """;
        command.Parameters.AddWithValue("$id", project.Id);
        command.Parameters.AddWithValue("$name", project.Name);
        command.Parameters.AddWithValue("$rootPath", project.RootPath);
        command.Parameters.AddWithValue("$updatedUtc", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
