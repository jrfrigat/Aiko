using System.ComponentModel;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;
using Aiko.Application.Agents;
using Aiko.Application.Contracts;
using Aiko.Infrastructure.Settings;
using Aiko.Infrastructure.Storage;
using Aiko.Server.Contracts;

namespace Aiko.Server.Mcp;

/// <summary>
/// Maintenance MCP tools backing the global /aiko-doctor, /aiko-repair, /aiko-settings,
/// /aiko-backup and /aiko-token skills.
/// </summary>
[McpServerToolType]
internal sealed class MaintenanceTools(
    IProjectCatalog catalog,
    IProjectReindexer reindexer,
    IAppSettingsService settings,
    AikoDataPaths dataPaths,
    DaemonEndpointConfiguration endpoints,
    IEnumerable<IAgentAdapter> adapters,
    AccessTokenStore tokenStore)
{
    [McpServerTool(Name = "aiko_doctor", Title = "Diagnose Aiko")]
    [Description("Returns a diagnostics summary: data directory, database, port, projects and detected agents.")]
    public async Task<string> DoctorAsync(CancellationToken cancellationToken)
    {
        var projects = await catalog.ListAsync(cancellationToken);
        var endpoint = await endpoints.TryReadAsync(cancellationToken);

        var summary = new StringBuilder()
            .AppendLine("Data directory: " + Path.GetDirectoryName(dataPaths.DatabasePath))
            .AppendLine("Database: " + dataPaths.DatabasePath)
            .AppendLine("Port: " + (endpoint?.Port.ToString() ?? "not started"))
            .AppendLine("Projects: " + projects.Count);
        foreach (var project in projects)
        {
            summary.AppendLine($"  - {project.Name} ({project.Id}) at {project.RootPath}");
        }

        foreach (var adapter in adapters)
        {
            var installations = await adapter.DetectInstallationsAsync(cancellationToken);
            summary.AppendLine(
                $"{adapter.DisplayName} ({adapter.Id}): " +
                (installations.Count == 0 ? "not found" : string.Join(", ", installations.Select(i => i.ExecutablePath))));
        }

        return summary.ToString();
    }

    [McpServerTool(Name = "aiko_reindex", Title = "Reindex Aiko project")]
    [Description("Rebuilds a project's SQLite projections from its .aiko files.")]
    public async Task<string> ReindexAsync(
        [Description("Project id.")]
        string projectId,
        CancellationToken cancellationToken)
    {
        var result = await reindexer.ReindexAsync(projectId, cancellationToken);
        return $"Reindexed project {projectId}: {result.Cards} cards, {result.Relations} relations, {result.MemoryDocuments} memory documents.";
    }

    [McpServerTool(Name = "aiko_backup", Title = "Backup Aiko project")]
    [Description("Creates a zip archive of the project's .aiko directory and returns its path.")]
    public async Task<string> BackupAsync(
        [Description("Project id.")]
        string projectId,
        CancellationToken cancellationToken)
    {
        var project = await catalog.FindAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Unknown Aiko project: {projectId}");
        var source = AikoProjectPaths.DataRoot(project.RootPath);
        if (!Directory.Exists(source))
        {
            throw new InvalidOperationException($"The project has no .aiko directory at {source}.");
        }

        var backupDirectory = Path.Combine(Path.GetDirectoryName(dataPaths.DatabasePath)!, "backups");
        Directory.CreateDirectory(backupDirectory);
        var target = Path.Combine(backupDirectory, $"{projectId}-{DateTime.UtcNow:yyyyMMddHHmmss}.zip");
        await Task.Run(() => ZipFile.CreateFromDirectory(source, target), cancellationToken);
        return $"Backup created: {target}";
    }

    [McpServerTool(Name = "aiko_token", Title = "Show Aiko access token")]
    [Description("Returns the local access token used to authenticate REST/MCP clients.")]
    public async Task<string> TokenAsync(CancellationToken cancellationToken) =>
        await tokenStore.GetOrCreateAsync(cancellationToken);

    [McpServerTool(Name = "aiko_get_settings", Title = "Get Aiko settings")]
    [Description("Returns the effective settings; pass a projectId for project settings, or omit for global.")]
    public async Task<string> GetSettingsAsync(
        [Description("Optional project id; omit for global settings.")]
        [Optional] string? projectId,
        CancellationToken cancellationToken)
    {
        var view = await settings.LoadAsync(projectId, cancellationToken);
        return JsonSerializer.Serialize(view, ServerJsonContext.Default.AppSettingsView);
    }
}