using Aiko.Application.Contracts;
using Aiko.Infrastructure.Storage;

namespace Aiko.Infrastructure.Memory;

/// <summary>
/// File-based store of the durable project memory: Markdown in .aiko/memory,
/// paths strictly inside the memory directory, full-text search over the SQLite projection.
/// </summary>
/// <remarks>
/// A caller may name the project by its id or by its readable handle; the projection is always keyed by
/// the id, the way a reindex writes it, so both names reach the same rows and a renamed handle loses
/// nothing.
/// </remarks>
public sealed class FileMemoryStore(
    IProjectCatalog projects,
    AikoDatabase database) : IMemoryStore
{
    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<MemoryDocument>> SearchAsync(
        string projectId,
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 100);

        var match = BuildMatchExpression(query);
        if (match is null)
        {
            return [];
        }

        var project = await EnsureProjectExistsAsync(projectId, cancellationToken);
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT path, content, updated_utc
            FROM memory_fts
            WHERE project_id = $projectId AND memory_fts MATCH $match
            ORDER BY updated_utc DESC, path
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$projectId", project.Id);
        command.Parameters.AddWithValue("$match", match);
        command.Parameters.AddWithValue("$limit", limit);

        var documents = new List<MemoryDocument>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            documents.Add(new MemoryDocument(
                reader.GetString(0),
                reader.GetString(1),
                DateTimeOffset.Parse(
                    reader.GetString(2),
                    System.Globalization.CultureInfo.InvariantCulture)));
        }

        return documents;
    }

    /// <inheritdoc />
    public async ValueTask StoreAsync(
        string projectId,
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var project = await EnsureProjectExistsAsync(projectId, cancellationToken);
        var (fullPath, relativePath) = ResolveMemoryPath(project.RootPath, path);
        var updatedAt = DateTimeOffset.UtcNow;
        await WriteAtomicallyAsync(fullPath, content, cancellationToken);
        await UpsertProjectionAsync(
            project.Id,
            relativePath,
            content,
            updatedAt,
            cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<MemoryDocumentSummary>> ListAsync(
        string projectId,
        CancellationToken cancellationToken)
    {
        var project = await EnsureProjectExistsAsync(projectId, cancellationToken);
        var memoryRoot = Path.GetFullPath(Path.Combine(AikoProjectPaths.DataRoot(project.RootPath), "memory"));
        if (!Directory.Exists(memoryRoot))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(memoryRoot, "*.md", SearchOption.AllDirectories)
            .Select(file => new FileInfo(file))
            .Select(info => new MemoryDocumentSummary(
                Path.GetRelativePath(memoryRoot, info.FullName).Replace(Path.DirectorySeparatorChar, '/'),
                info.Length,
                new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero)))
            .OrderBy(summary => summary.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<MemoryDocument?> ReadAsync(
        string projectId,
        string path,
        CancellationToken cancellationToken)
    {
        var project = await EnsureProjectExistsAsync(projectId, cancellationToken);
        var (fullPath, relativePath) = ResolveMemoryPath(project.RootPath, path);
        if (!File.Exists(fullPath))
        {
            return null;
        }

        return new MemoryDocument(
            relativePath,
            await File.ReadAllTextAsync(fullPath, cancellationToken),
            new DateTimeOffset(File.GetLastWriteTimeUtc(fullPath), TimeSpan.Zero));
    }

    /// <inheritdoc />
    public async ValueTask RemoveAsync(
        string projectId,
        string path,
        CancellationToken cancellationToken)
    {
        var project = await EnsureProjectExistsAsync(projectId, cancellationToken);
        var (fullPath, relativePath) = ResolveMemoryPath(project.RootPath, path);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            DELETE FROM memory_fts
            WHERE project_id = $projectId AND path = $path;
            """;
        command.Parameters.AddWithValue("$projectId", project.Id);
        command.Parameters.AddWithValue("$path", relativePath);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Resolves a relative Markdown path to a full path inside .aiko/memory plus a
    /// normalized relative path; rejects escapes outside the directory.
    /// </summary>
    internal static (string FullPath, string RelativePath) ResolveMemoryPath(
        string projectRoot,
        string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalizedRelativePath = path.Replace(
            Path.AltDirectorySeparatorChar,
            Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalizedRelativePath) ||
            !StringComparer.OrdinalIgnoreCase.Equals(
                Path.GetExtension(normalizedRelativePath),
                ".md"))
        {
            throw new ArgumentException(
                "Memory path must be a relative Markdown path.",
                nameof(path));
        }

        var memoryRoot = Path.GetFullPath(
            Path.Combine(AikoProjectPaths.DataRoot(projectRoot), "memory"));
        var fullPath = Path.GetFullPath(Path.Combine(memoryRoot, normalizedRelativePath));
        var safePrefix = Path.TrimEndingDirectorySeparator(memoryRoot) +
            Path.DirectorySeparatorChar;
        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!fullPath.StartsWith(safePrefix, pathComparison))
        {
            throw new ArgumentException(
                "Memory path must stay inside .aiko/memory.",
                nameof(path));
        }

        // Inside by its name is not inside on the disk: a junction under .aiko/memory would take the write
        // anywhere, so the way down is checked too.
        PathConfinement.Resolve(AikoProjectPaths.DataRoot(projectRoot), fullPath);
        var relativePath = Path.GetRelativePath(memoryRoot, fullPath)
            .Replace(Path.DirectorySeparatorChar, '/');
        return (fullPath, relativePath);
    }

    /// <summary>
    /// Converts a free-text query into an FTS5 prefix-match expression where every
    /// token must match as a term prefix; null when the query has no searchable tokens.
    /// </summary>
    private static string? BuildMatchExpression(string query)
    {
        var tokens = query
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Any(char.IsLetterOrDigit))
            .Select(token => $"\"{token.Replace("\"", "\"\"")}\"*")
            .ToArray();
        return tokens.Length == 0 ? null : string.Join(' ', tokens);
    }

    private async ValueTask<RegisteredProject> EnsureProjectExistsAsync(
        string projectId,
        CancellationToken cancellationToken) =>
        await projects.FindAsync(projectId, cancellationToken)
        ?? throw new KeyNotFoundException($"Unknown Aiko project: {projectId}");

    private static async ValueTask WriteAtomicallyAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Memory path must include a directory."));
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, cancellationToken);
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private async ValueTask UpsertProjectionAsync(
        string projectId,
        string path,
        string content,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText =
                "DELETE FROM memory_fts WHERE project_id = $projectId AND path = $path;";
            delete.Parameters.AddWithValue("$projectId", projectId);
            delete.Parameters.AddWithValue("$path", path);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO memory_fts(project_id, path, content, updated_utc)
                VALUES ($projectId, $path, $content, $updatedUtc);
                """;
            insert.Parameters.AddWithValue("$projectId", projectId);
            insert.Parameters.AddWithValue("$path", path);
            insert.Parameters.AddWithValue("$content", content);
            insert.Parameters.AddWithValue("$updatedUtc", updatedAt.ToString("O"));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
