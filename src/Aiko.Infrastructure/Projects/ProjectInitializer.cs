using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Aiko.Application.Contracts;
using Aiko.Domain.Execution;
using Aiko.Domain.Prioritization;
using Aiko.Infrastructure.Storage;

namespace Aiko.Infrastructure.Projects;

/// <summary>
/// Project initializer: creates the .aiko structure, applies the chosen template (workflows, projections,
/// starting memory, default settings), applies the gitignore policy and runs the initial reindex.
/// </summary>
/// <remarks>
/// The template is the only source of "what a project starts as". The workflows, projections and memory
/// files that used to be hardcoded here now live in <see cref="BuiltInProjectTemplate"/>, and the settings
/// come from the template's own section or, when it leaves them out, from the global application settings.
/// Nothing is ever overwritten: a file that already exists in the project belongs to the user, so re-running
/// an init fills gaps and records provenance rather than resetting a working project.
/// </remarks>
public sealed class ProjectInitializer(
    IProjectCatalog catalog,
    IProjectReindexer reindexer,
    IAppSettingsStore settings,
    IProjectTemplateStore templates) : IProjectInitializer
{
    private const int CurrentSchemaVersion = 1;

    /// <summary>
    /// Global lock serializing concurrent project initializations.
    /// </summary>
    private static readonly SemaphoreSlim InitializationLock = new(1, 1);

    /// <inheritdoc />
    public async ValueTask<RegisteredProject> InitializeAsync(
        InitializeProjectRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RootPath);

        var rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.RootPath));
        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"Project directory does not exist: {rootPath}");
        }

        await InitializationLock.WaitAsync(cancellationToken);
        try
        {
            return await InitializeCoreAsync(request, rootPath, cancellationToken);
        }
        finally
        {
            InitializationLock.Release();
        }
    }

    private async ValueTask<RegisteredProject> InitializeCoreAsync(
        InitializeProjectRequest request,
        string rootPath,
        CancellationToken cancellationToken)
    {
        var stitchRoot = AikoProjectPaths.DataRoot(rootPath);
        CreateDirectories(stitchRoot);

        // The template is resolved before the manifest, because the manifest records which template built
        // the project. An init without a choice uses the default template, which is written on first use.
        var template = request.TemplateId is { Length: > 0 } templateId
            ? await templates.ReadAsync(templateId, cancellationToken)
            : await templates.EnsureDefaultAsync(cancellationToken);

        var manifestPath = Path.Combine(stitchRoot, "project.json");
        var manifest = await ReadManifestAsync(manifestPath, cancellationToken)
            ?? new ProjectManifest(
                CurrentSchemaVersion,
                Guid.CreateVersion7().ToString("N"),
                NormalizeProjectName(request.Name, rootPath),
                rootPath,
                request.GitPolicy,
                DateTimeOffset.UtcNow,
                template.Id,
                template.Version);

        await WriteNewJsonAsync(
            manifestPath,
            manifest,
            ProjectJsonContext.Default.ProjectManifest,
            cancellationToken);

        await ApplyTemplateAsync(stitchRoot, template, cancellationToken);
        await WriteNewJsonAsync(
            Path.Combine(stitchRoot, "relations.json"),
            new RelationDocument(CurrentSchemaVersion, 0, []),
            ProjectJsonContext.Default.RelationDocument,
            cancellationToken);
        await ApplyGitPolicyAsync(rootPath, manifest.GitPolicy, AikoProjectPaths.DirectoryName, cancellationToken);

        var project = new RegisteredProject(manifest.Id, manifest.Name, rootPath);
        await catalog.SaveAsync(project, cancellationToken);

        // The settings snapshot is taken once, at init: afterwards the project's own file is authoritative
        // and editing the template or the global settings does not reach it.
        await CopyDefaultSettingsAsync(project, template, cancellationToken);

        await reindexer.ReindexAsync(project.Id, cancellationToken);
        return project;
    }

    /// <summary>
    /// Writes the template's documents into the project, skipping everything that already exists.
    /// </summary>
    private static async ValueTask ApplyTemplateAsync(
        string stitchRoot,
        ProjectTemplate template,
        CancellationToken cancellationToken)
    {
        foreach (var workflow in template.Workflows)
        {
            await WriteNewJsonAsync(
                Path.Combine(stitchRoot, "workflows", $"{workflow.Id}.json"),
                workflow,
                ProjectJsonContext.Default.WorkflowDefinition,
                cancellationToken);
        }

        foreach (var projection in template.Projections)
        {
            await WriteNewJsonAsync(
                Path.Combine(stitchRoot, "projections", $"{projection.Id}.json"),
                projection,
                ProjectJsonContext.Default.BoardProjectionDefinition,
                cancellationToken);
        }

        foreach (var document in template.MemoryFiles)
        {
            var path = Path.Combine(stitchRoot, document.RelativePath);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await WriteNewTextAsync(path, document.Content, cancellationToken);
        }
    }

    /// <summary>
    /// Gives the project its settings snapshot: the template's sections over the built-in defaults, so the
    /// project states every value it runs with and a later release cannot change its behaviour.
    /// </summary>
    private async ValueTask CopyDefaultSettingsAsync(
        RegisteredProject project,
        ProjectTemplate template,
        CancellationToken cancellationToken)
    {
        if (await settings.ReadProjectAsync(project.Id, cancellationToken) is not null)
        {
            return;
        }

        await settings.SaveProjectAsync(
            project.Id,
            new AppSettings(
                AppSettings.CurrentSchemaVersion,
                template.Settings?.Execution ?? ExecutionSettings.SafeDefault,
                template.Settings?.Priority ?? PrioritySettings.SafeDefault),
            cancellationToken);
    }

    private static void CreateDirectories(string stitchRoot)
    {
        string[] relativePaths =
        [
            "workflows",
            "projections",
            "stories",
            "tasks",
            "memory",
            "runtime"
        ];

        Directory.CreateDirectory(stitchRoot);
        foreach (var relativePath in relativePaths)
        {
            Directory.CreateDirectory(Path.Combine(stitchRoot, relativePath));
        }
    }

    private static async ValueTask<ProjectManifest?> ReadManifestAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        await using var input = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync(
            input,
            ProjectJsonContext.Default.ProjectManifest,
            cancellationToken);

        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id))
        {
            throw new InvalidDataException($"Invalid Aiko project manifest: {manifestPath}");
        }

        if (manifest.SchemaVersion > CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Project schema {manifest.SchemaVersion} is newer than supported schema {CurrentSchemaVersion}.");
        }

        return manifest;
    }

    private static string NormalizeProjectName(string? requestedName, string rootPath)
    {
        var name = string.IsNullOrWhiteSpace(requestedName)
            ? Path.GetFileName(rootPath)
            : requestedName.Trim();
        return string.IsNullOrWhiteSpace(name) ? "Project" : name;
    }

    private static async ValueTask ApplyGitPolicyAsync(
        string rootPath,
        ProjectGitPolicy policy,
        string dataDirectory,
        CancellationToken cancellationToken)
    {
        if (policy == ProjectGitPolicy.Custom)
        {
            return;
        }

        // The agent MCP configurations carry the daemon's access token, so they are secrets by definition
        // and never belong in a commit - whatever the policy says about the rest of the project.
        string[] entries =
        [
            policy == ProjectGitPolicy.LocalOnly
                ? $"/{dataDirectory}/"
                : $"/{dataDirectory}/runtime/",
            "/.mcp.json",
            "/.cursor/mcp.json",
            "/.zcode/config.json"
        ];

        var gitIgnorePath = Path.Combine(rootPath, ".gitignore");
        var existingText = File.Exists(gitIgnorePath)
            ? await File.ReadAllTextAsync(gitIgnorePath, cancellationToken)
            : string.Empty;
        var existingLines = existingText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);

        var missing = entries.Where(entry => !existingLines.Contains(entry)).ToArray();
        if (missing.Length == 0)
        {
            return;
        }

        var separator = existingText.Length > 0 && !existingText.EndsWith('\n')
            ? Environment.NewLine
            : string.Empty;
        await File.AppendAllTextAsync(
            gitIgnorePath,
            $"{separator}{string.Join(Environment.NewLine, missing)}{Environment.NewLine}",
            cancellationToken);
    }

    private static async ValueTask WriteNewJsonAsync<T>(
        string path,
        T value,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        if (File.Exists(path))
        {
            return;
        }

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
                await JsonSerializer.SerializeAsync(output, value, typeInfo, cancellationToken);
            }

            File.Move(temporaryPath, path, false);
        }
        catch (IOException) when (File.Exists(path))
        {
            File.Delete(temporaryPath);
        }
    }

    private static async ValueTask WriteNewTextAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        if (File.Exists(path))
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(path, content, cancellationToken);
        }
        catch (IOException) when (File.Exists(path))
        {
            // Another initializer won the race; the existing user-owned file is authoritative.
        }
    }
}
