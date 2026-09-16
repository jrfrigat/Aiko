using System.Text.Json;
using Aiko.Application.Contracts;
using Aiko.Infrastructure.Projects;
using Aiko.Infrastructure.Storage;

namespace Aiko.Infrastructure.Settings;

/// <summary>
/// File-based application settings: the global file next to the daemon database and the
/// project-local .aiko/settings.json, both written atomically with source-generated JSON.
/// </summary>
public sealed class FileAppSettingsStore(
    AikoDataPaths paths,
    IProjectCatalog projects) : IAppSettingsStore
{
    /// <inheritdoc />
    public async ValueTask<AppSettings?> ReadGlobalAsync(CancellationToken cancellationToken)
    {
        var path = paths.ApplicationSettingsPath;
        if (!File.Exists(path))
        {
            return null;
        }

        await using var input = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync(
            input,
            SettingsJsonContext.Default.AppSettings,
            cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask SaveGlobalAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await WriteAtomicallyAsync(paths.ApplicationSettingsPath, settings, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<AppSettings?> ReadProjectAsync(
        string projectId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        var path = await GetProjectSettingsPathAsync(projectId, cancellationToken);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var input = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync(
            input,
            SettingsJsonContext.Default.AppSettings,
            cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask SaveProjectAsync(
        string projectId,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentNullException.ThrowIfNull(settings);
        var path = await GetProjectSettingsPathAsync(projectId, cancellationToken);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await WriteAtomicallyAsync(path, settings, cancellationToken);
    }

    private async ValueTask<string> GetProjectSettingsPathAsync(
        string projectId,
        CancellationToken cancellationToken)
    {
        var project = await projects.FindAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Unknown Aiko project: {projectId}");
        return Path.Combine(AikoProjectPaths.DataRoot(project.RootPath), "settings.json");
    }

    private static async ValueTask WriteAtomicallyAsync(
        string path,
        AppSettings settings,
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
                    settings,
                    SettingsJsonContext.Default.AppSettings,
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
}
