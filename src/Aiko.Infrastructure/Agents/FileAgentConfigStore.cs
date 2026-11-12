using System.Text.Json;
using Aiko.Application.Agents;
using Aiko.Application.Contracts;
using Aiko.Infrastructure.Projects;
using Aiko.Infrastructure.Storage;

namespace Aiko.Infrastructure.Agents;

/// <summary>
/// File-based agent configuration: the user-level directory beside the daemon's data and the project's own
/// <c>.aiko/agents</c>, written atomically with source-generated JSON.
/// </summary>
public sealed class FileAgentConfigStore(
    IProjectCatalog projects,
    AikoDataPaths dataPaths) : IAgentConfigStore
{
    private readonly string userDirectory = Path.Combine(
        Path.GetDirectoryName(dataPaths.DatabasePath)
            ?? throw new InvalidOperationException("The database path must include a directory."),
        "agents");

    /// <inheritdoc />
    public ValueTask<AgentConfig?> ReadUserAsync(
        string adapterId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ReadAsync(Path.Combine(userDirectory, $"{ValidateId(adapterId)}.json"), cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<AgentConfig?> ReadProjectAsync(
        string projectId,
        string adapterId,
        CancellationToken cancellationToken)
    {
        var directory = await ProjectDirectoryAsync(projectId, cancellationToken);
        return await ReadAsync(
            Path.Combine(directory, $"{ValidateId(adapterId)}.json"),
            cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask SaveUserAsync(AgentConfig config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);
        Directory.CreateDirectory(userDirectory);
        await WriteAsync(
            Path.Combine(userDirectory, $"{ValidateId(config.Id)}.json"),
            config,
            cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask SaveProjectAsync(
        string projectId,
        AgentConfig config,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);
        var directory = await ProjectDirectoryAsync(projectId, cancellationToken);
        Directory.CreateDirectory(directory);
        await WriteAsync(
            Path.Combine(directory, $"{ValidateId(config.Id)}.json"),
            config,
            cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<string>> ListUserAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(List(userDirectory));
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<string>> ListProjectAsync(
        string projectId,
        CancellationToken cancellationToken)
    {
        var directory = await ProjectDirectoryAsync(projectId, cancellationToken);
        return List(directory);
    }

    private async ValueTask<string> ProjectDirectoryAsync(
        string projectId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        var project = await projects.FindAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Unknown Aiko project: {projectId}");
        return Path.Combine(AikoProjectPaths.DataRoot(project.RootPath), "agents");
    }

    private static async ValueTask<AgentConfig?> ReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        await using var input = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync(
            input,
            AgentConfigJsonContext.Default.AgentConfig,
            cancellationToken);
    }

    private static async ValueTask WriteAsync(
        string path,
        AgentConfig config,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(
                    output,
                    config,
                    AikoJson.AgentConfig,
                    cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static IReadOnlyList<string> List(string directory) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];

    private static string ValidateId(string? adapterId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adapterId);
        var trimmed = adapterId.Trim();
        if (trimmed.Length > 64 ||
            !trimmed.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
        {
            throw new ArgumentException(
                $"'{adapterId}' is not a usable agent id: letters, digits, '-' and '_' only.",
                nameof(adapterId));
        }

        return trimmed;
    }
}
