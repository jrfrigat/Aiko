using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
using Aiko.Domain.Workflow;
using Aiko.Infrastructure.Storage;

namespace Aiko.Infrastructure.Projects;

/// <summary>
/// Project initializer: creates the .aiko structure, default documents
/// (workflows, projections, memory), applies the gitignore policy and runs the initial reindex.
/// </summary>
public sealed class ProjectInitializer(
    IProjectCatalog catalog,
    IProjectReindexer reindexer,
    IAppSettingsStore settings) : IProjectInitializer
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

        var manifestPath = Path.Combine(stitchRoot, "project.json");
        var manifest = await ReadManifestAsync(manifestPath, cancellationToken)
            ?? new ProjectManifest(
                CurrentSchemaVersion,
                Guid.CreateVersion7().ToString("N"),
                NormalizeProjectName(request.Name, rootPath),
                rootPath,
                request.GitPolicy,
                DateTimeOffset.UtcNow);

        await WriteNewJsonAsync(
            manifestPath,
            manifest,
            ProjectJsonContext.Default.ProjectManifest,
            cancellationToken);

        await WriteDefaultsAsync(stitchRoot, cancellationToken);
        await WriteNewJsonAsync(
            Path.Combine(stitchRoot, "relations.json"),
            new RelationDocument(CurrentSchemaVersion, 0, []),
            ProjectJsonContext.Default.RelationDocument,
            cancellationToken);
        await ApplyGitPolicyAsync(rootPath, manifest.GitPolicy, AikoProjectPaths.DirectoryName, cancellationToken);

        var project = new RegisteredProject(manifest.Id, manifest.Name, rootPath);
        await catalog.SaveAsync(project, cancellationToken);

        // Copy the global defaults into the project on first initialization; afterwards the
        // project settings are authoritative and editing globals does not affect it.
        await CopyGlobalDefaultsAsync(project, cancellationToken);

        await reindexer.ReindexAsync(project.Id, cancellationToken);
        return project;
    }

    private async ValueTask CopyGlobalDefaultsAsync(
        RegisteredProject project,
        CancellationToken cancellationToken)
    {
        var global = await settings.ReadGlobalAsync(cancellationToken);
        if (global is null)
        {
            return;
        }

        var existing = await settings.ReadProjectAsync(project.Id, cancellationToken);
        if (existing is null)
        {
            await settings.SaveProjectAsync(project.Id, global, cancellationToken);
        }
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

    private static async ValueTask WriteDefaultsAsync(
        string stitchRoot,
        CancellationToken cancellationToken)
    {
        await WriteNewJsonAsync(
            Path.Combine(stitchRoot, "workflows", "story.json"),
            CreateStoryWorkflow(),
            ProjectJsonContext.Default.WorkflowDefinition,
            cancellationToken);
        await WriteNewJsonAsync(
            Path.Combine(stitchRoot, "workflows", "task.json"),
            CreateTaskWorkflow(),
            ProjectJsonContext.Default.WorkflowDefinition,
            cancellationToken);

        foreach (var projection in CreateProjections())
        {
            await WriteNewJsonAsync(
                Path.Combine(stitchRoot, "projections", $"{projection.Id}.json"),
                projection,
                ProjectJsonContext.Default.BoardProjectionDefinition,
                cancellationToken);
        }

        await WriteNewTextAsync(
            Path.Combine(stitchRoot, "memory", "index.md"),
            "# Память проекта\n\nЭтот индекс содержит ссылки на устойчивые знания проекта.\n",
            cancellationToken);
        await WriteNewTextAsync(
            Path.Combine(stitchRoot, "memory", "architecture.md"),
            "# Архитектура\n\n",
            cancellationToken);
        await WriteNewTextAsync(
            Path.Combine(stitchRoot, "memory", "conventions.md"),
            "# Соглашения\n\n",
            cancellationToken);
        await WriteNewTextAsync(
            Path.Combine(stitchRoot, "memory", "lessons.md"),
            "# Накопленный опыт\n\n",
            cancellationToken);
    }

    private static WorkflowDefinition CreateStoryWorkflow() =>
        new(
            "story",
            "Stories",
            [
                Stage("backlog", "Бэклог", 10, CardKind.Story, "Уточни ценность, границы и связи story."),
                Stage(
                    "elaboration",
                    "Проработка",
                    20,
                    CardKind.Story,
                    "Проработай требования и архитектурные ограничения story.",
                    [
                        new ArtifactRequirement(
                            "analysis.md",
                            "Результат проработки story.",
                            MissingArtifactPolicy.NeedsAttention)
                    ]),
                Stage("ready", "Готово к декомпозиции", 30, CardKind.Story, "Проверь готовность story к декомпозиции."),
                Stage("in-progress", "В работе", 40, CardKind.Story, "Координируй реализацию дочерних задач."),
                Stage("done", "Завершено", 50, CardKind.Story, "Проверь достижение результата story.")
            ],
            1);

    private static WorkflowDefinition CreateTaskWorkflow() =>
        new(
            "task",
            "Tasks",
            [
                Stage("backlog", "Бэклог", 10, CardKind.Task, "Уточни запрос, scope и связи задачи."),
                Stage(
                    "analysis",
                    "Анализ",
                    20,
                    CardKind.Task,
                    "Проанализируй задачу, риски и варианты реализации.",
                    [
                        new ArtifactRequirement(
                            "analysis.md",
                            "Результат анализа задачи.",
                            MissingArtifactPolicy.NeedsAttention)
                    ]),
                Stage(
                    "implementation",
                    "Реализация",
                    30,
                    CardKind.Task,
                    "Реализуй задачу и зафиксируй фактически измененные файлы.",
                    [
                        new ArtifactRequirement(
                            "implementation.md",
                            "Итог реализации и проверки.",
                            MissingArtifactPolicy.Warn)
                    ]),
                Stage("review", "Проверка", 40, CardKind.Task, "Проверь результат, тесты и отклонения от scope."),
                Stage("done", "Завершено", 50, CardKind.Task, "Зафиксируй итог выполнения задачи.")
            ],
            1);

    private static StageDefinition Stage(
        string id,
        string title,
        int order,
        CardKind kind,
        string instruction,
        IReadOnlyList<ArtifactRequirement>? artifacts = null) =>
        new(
            id,
            title,
            order,
            instruction,
            [kind],
            null,
            artifacts ?? [],
            new Dictionary<string, ActionPolicy>(StringComparer.Ordinal));

    private static IReadOnlyList<BoardProjectionDefinition> CreateProjections() =>
    [
        new(CurrentSchemaVersion, "tasks", "Tasks", "kanban", "task", "stage", EmptyFilters()),
        new(CurrentSchemaVersion, "stories", "Stories", "kanban", "story", "stage", EmptyFilters()),
        new(CurrentSchemaVersion, "combined", "Combined", "swimlane", null, "story", EmptyFilters())
    ];

    private static IReadOnlyDictionary<string, string> EmptyFilters() =>
        new Dictionary<string, string>(StringComparer.Ordinal);

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

        var ignoreEntry = policy == ProjectGitPolicy.LocalOnly
            ? $"/{dataDirectory}/"
            : $"/{dataDirectory}/runtime/";
        var gitIgnorePath = Path.Combine(rootPath, ".gitignore");
        var existingText = File.Exists(gitIgnorePath)
            ? await File.ReadAllTextAsync(gitIgnorePath, cancellationToken)
            : string.Empty;
        var existingLines = existingText.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (existingLines.Contains(ignoreEntry, StringComparer.Ordinal))
        {
            return;
        }

        var separator = existingText.Length > 0 && !existingText.EndsWith('\n') ? Environment.NewLine : string.Empty;
        await File.AppendAllTextAsync(
            gitIgnorePath,
            $"{separator}{ignoreEntry}{Environment.NewLine}",
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
