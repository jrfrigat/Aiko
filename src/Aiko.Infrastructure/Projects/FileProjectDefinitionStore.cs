using System.Text.Json;
using Aiko.Application.Contracts;
using Aiko.Domain.Workflow;
using Aiko.Infrastructure.Storage;
using Aiko.Infrastructure.Threading;

namespace Aiko.Infrastructure.Projects;

/// <summary>
/// File-based store of project definitions: workflows from .aiko/workflows and board
/// projections from .aiko/projections with atomic writes and revisions.
/// </summary>
public sealed class FileProjectDefinitionStore(IProjectCatalog projects) : IProjectDefinitionStore
{
    /// <summary>
    /// Reference-counted per-workflow locks; idle keys are dropped automatically.
    /// </summary>
    private readonly KeyedLockStore locks = new();

    /// <inheritdoc />
    public async ValueTask<ProjectBoardDefinition> ReadAsync(
        string projectId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        var project = await projects.FindAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Unknown Aiko project: {projectId}");
        var stitchRoot = AikoProjectPaths.DataRoot(project.RootPath);

        var workflows = await ReadDocumentsAsync(
            Path.Combine(stitchRoot, "workflows"),
            ProjectJsonContext.Default.WorkflowDefinition,
            cancellationToken,
            ValidateIdentifiers);
        var projections = await ReadDocumentsAsync(
            Path.Combine(stitchRoot, "projections"),
            ProjectJsonContext.Default.BoardProjectionDefinition,
            cancellationToken);

        return new ProjectBoardDefinition(
            workflows.OrderBy(workflow => workflow.Title, StringComparer.Ordinal).ToArray(),
            projections.OrderBy(projection => projection.Title, StringComparer.Ordinal).ToArray());
    }

    /// <inheritdoc />
    public async ValueTask SaveWorkflowAsync(
        string projectId,
        WorkflowDefinition workflow,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);
        ValidateWorkflow(workflow);
        if (workflow.Revision != expectedRevision + 1)
        {
            throw new ArgumentException(
                $"Saved workflow revision must be {expectedRevision + 1}.",
                nameof(workflow));
        }

        var project = await projects.FindAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Unknown Aiko project: {projectId}");
        var workflowPath = Path.Combine(
            AikoProjectPaths.DataRoot(project.RootPath),
            "workflows",
            $"{workflow.Id}.json");

        using (await locks.LockAsync($"{projectId}/{workflow.Id}", cancellationToken))
        {
            var actualRevision = File.Exists(workflowPath)
                ? (await ReadDocumentAsync(
                    workflowPath,
                    ProjectJsonContext.Default.WorkflowDefinition,
                    cancellationToken)).Revision
                : 0;
            if (actualRevision != expectedRevision)
            {
                throw new RevisionConflictException(
                    $"workflow {projectId}/{workflow.Id}",
                    expectedRevision,
                    actualRevision);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(workflowPath)!);
            await WriteAtomicallyAsync(workflowPath, workflow, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async ValueTask CreateWorkflowAsync(
        string projectId,
        WorkflowDefinition workflow,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentNullException.ThrowIfNull(workflow);
        ValidateWorkflow(workflow);
        if (workflow.Revision != 1)
        {
            throw new ArgumentException("A created workflow starts at revision 1.", nameof(workflow));
        }

        var project = await projects.FindAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Unknown Aiko project: {projectId}");
        var workflowPath = WorkflowPath(project.RootPath, workflow.Id);

        using (await locks.LockAsync($"{projectId}/{workflow.Id}", cancellationToken))
        {
            if (File.Exists(workflowPath))
            {
                throw new InvalidOperationException($"Workflow '{workflow.Id}' already exists.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(workflowPath)!);
            await WriteAtomicallyAsync(workflowPath, workflow, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async ValueTask DeleteWorkflowAsync(
        string projectId,
        string workflowId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);

        var project = await projects.FindAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Unknown Aiko project: {projectId}");
        var workflowPath = WorkflowPath(project.RootPath, workflowId);

        using (await locks.LockAsync($"{projectId}/{workflowId}", cancellationToken))
        {
            if (!File.Exists(workflowPath))
            {
                throw new KeyNotFoundException($"Workflow '{workflowId}' does not exist.");
            }

            File.Delete(workflowPath);
        }
    }

    /// <inheritdoc />
    public async ValueTask<WorkflowDefinition> RenameWorkflowAsync(
        string projectId,
        string currentId,
        string newId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newId);

        var normalizedId = newId.Trim().ToLowerInvariant();
        FileSystemSafeIdentifiers.Validate(normalizedId, "workflow");

        var project = await projects.FindAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Unknown Aiko project: {projectId}");
        var definition = await ReadAsync(projectId, cancellationToken);
        var workflow = definition.Workflows.FirstOrDefault(candidate =>
            StringComparer.Ordinal.Equals(candidate.Id, currentId))
            ?? throw new KeyNotFoundException($"Workflow '{currentId}' does not exist.");
        if (StringComparer.Ordinal.Equals(workflow.Id, normalizedId))
        {
            return workflow;
        }

        if (definition.Workflows.Any(candidate =>
                StringComparer.Ordinal.Equals(candidate.Id, normalizedId)))
        {
            throw new InvalidOperationException($"Workflow '{normalizedId}' already exists.");
        }

        // The revision counts the edits made here, and a rename is one of them.
        var renamed = workflow with { Id = normalizedId, Revision = workflow.Revision + 1 };
        var currentPath = WorkflowPath(project.RootPath, currentId);
        var renamedPath = WorkflowPath(project.RootPath, normalizedId);
        using (await locks.LockAsync($"{projectId}/{currentId}", cancellationToken))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(renamedPath)!);
            await WriteAtomicallyAsync(renamedPath, renamed, cancellationToken);
            if (File.Exists(currentPath))
            {
                File.Delete(currentPath);
            }
        }

        await RenameProjectionKindAsync(project.RootPath, currentId, normalizedId, cancellationToken);
        return renamed;
    }

    /// <summary>
    /// Points the board projections that showed a card type at its new id.
    /// </summary>
    /// <remarks>
    /// A projection's card kind IS the workflow id, so a rename that left them behind would drop the type's
    /// cards out of the board section that used to show them - silently, because the projection would still
    /// look valid.
    /// </remarks>
    private static async ValueTask RenameProjectionKindAsync(
        string projectRoot,
        string currentId,
        string newId,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(AikoProjectPaths.DataRoot(projectRoot), "projections");
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal))
        {
            BoardProjectionDefinition projection;
            await using (var input = File.OpenRead(path))
            {
                projection = await JsonSerializer.DeserializeAsync(
                    input,
                    ProjectJsonContext.Default.BoardProjectionDefinition,
                    cancellationToken)
                    ?? throw new InvalidDataException($"Invalid Aiko document: {path}");
            }

            if (!StringComparer.Ordinal.Equals(projection.CardKind, currentId))
            {
                continue;
            }

            await WriteAtomicallyAsync(path, projection with { CardKind = newId }, cancellationToken);
        }
    }

    private static string WorkflowPath(string projectRoot, string workflowId) =>
        PathConfinement.Resolve(
            AikoProjectPaths.DataRoot(projectRoot),
            Path.Combine(AikoProjectPaths.DataRoot(projectRoot), "workflows", $"{workflowId}.json"));

    /// <summary>
    /// Refuses a workflow read from disk whose identifiers could not be file names: its id, its stages and the
    /// card kinds it takes become directory and command names, and the file is data a repository carries.
    /// </summary>
    private static void ValidateIdentifiers(WorkflowDefinition workflow)
    {
        FileSystemSafeIdentifiers.Validate(workflow.Id, "workflow");
        foreach (var stage in workflow.Stages ?? [])
        {
            FileSystemSafeIdentifiers.Validate(stage.Id, "stage");
            foreach (var kind in stage.AllowedCardKinds ?? [])
            {
                FileSystemSafeIdentifiers.Validate(kind, "card kind");
            }
        }
    }

    /// <summary>
    /// The title of one workflow, read from its own document by the project root alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Synchronous and by root rather than through <see cref="IProjectDefinitionStore"/> on purpose: the card
    /// store names a card's collection after the workflow that defines its type, and its path helpers are
    /// static - they are called from stores that hold no definition store. Reading the one document here keeps
    /// the naming rule from having to know the document's schema as well.
    /// </para>
    /// <para>
    /// A missing or unreadable document reads as null rather than throwing: a project whose definition was
    /// removed still holds the cards that were filed under it, and a store that failed to list them would be
    /// the worse answer.
    /// </para>
    /// </remarks>
    /// <param name="projectRoot">The project's root directory.</param>
    /// <param name="workflowId">The workflow's id, which is the card type's own id.</param>
    internal static string? ReadWorkflowTitle(string projectRoot, string workflowId)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) || string.IsNullOrWhiteSpace(workflowId))
        {
            return null;
        }

        try
        {
            using var input = File.OpenRead(WorkflowPath(projectRoot, workflowId));
            return JsonSerializer.Deserialize(input, ProjectJsonContext.Default.WorkflowDefinition)?.Title;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static async ValueTask<IReadOnlyList<T>> ReadDocumentsAsync<T>(
        string directory,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken,
        Action<T>? validate = null)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var documents = new List<T>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal))
        {
            await using var input = File.OpenRead(path);
            var document = await JsonSerializer.DeserializeAsync(input, typeInfo, cancellationToken)
                ?? throw new InvalidDataException($"Invalid Aiko document: {path}");
            try
            {
                validate?.Invoke(document);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException($"Invalid Aiko document {path}: {exception.Message}", exception);
            }

            documents.Add(document);
        }

        return documents;
    }

    private static async ValueTask<T> ReadDocumentAsync<T>(
        string path,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        await using var input = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync(input, typeInfo, cancellationToken)
            ?? throw new InvalidDataException($"Invalid Aiko document: {path}");
    }

    private static async ValueTask WriteAtomicallyAsync<T>(
        string path,
        T document,
        CancellationToken cancellationToken)
    {
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
                    document,
                    AikoJson.Project,
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

    private static void ValidateWorkflow(WorkflowDefinition workflow)
    {
        FileSystemSafeIdentifiers.Validate(workflow.Id, "workflow");
        ArgumentException.ThrowIfNullOrWhiteSpace(workflow.Title);
        if (workflow.Stages is null || workflow.Stages.Count == 0)
        {
            throw new ArgumentException("A workflow must contain at least one stage.", nameof(workflow));
        }

        if (!AppearanceCatalog.IsValidIcon(workflow.Icon) || !AppearanceCatalog.IsValidColor(workflow.Color))
        {
            throw new ArgumentException(
                "Workflow icon and color must come from the appearance catalog.",
                nameof(workflow));
        }

        var stageIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var stage in workflow.Stages)
        {
            FileSystemSafeIdentifiers.Validate(stage.Id, "stage");
            ArgumentException.ThrowIfNullOrWhiteSpace(stage.Title);
            if (!stageIds.Add(stage.Id))
            {
                throw new ArgumentException($"Duplicate workflow stage: {stage.Id}", nameof(workflow));
            }

            if (stage.Instruction is null || stage.AllowedCardKinds is null || stage.AllowedCardKinds.Count == 0)
            {
                throw new ArgumentException(
                    $"Workflow stage {stage.Id} must allow at least one card kind.",
                    nameof(workflow));
            }

            // The workflow defines one card type, so every one of its stages has to accept it - otherwise a
            // card of that type could be created and then be unable to move at all.
            if (!stage.AllowedCardKinds.Contains(workflow.CardType, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Workflow stage {stage.Id} must allow the card type {workflow.CardType}.",
                    nameof(workflow));
            }

            if (!AppearanceCatalog.IsValidIcon(stage.Icon) || !AppearanceCatalog.IsValidColor(stage.Color))
            {
                throw new ArgumentException(
                    $"Workflow stage {stage.Id} icon and color must come from the appearance catalog.",
                    nameof(workflow));
            }

            if (stage.RequiredArtifacts is null || stage.ActionPolicies is null)
            {
                throw new ArgumentException(
                    $"Workflow stage {stage.Id} has incomplete settings.",
                    nameof(workflow));
            }

            foreach (var artifact in stage.RequiredArtifacts)
            {
                if (string.IsNullOrWhiteSpace(artifact.Path) ||
                    Path.IsPathRooted(artifact.Path) ||
                    artifact.Path.Replace('\\', '/').Split('/').Contains("..", StringComparer.Ordinal))
                {
                    throw new ArgumentException(
                        $"Artifact path must stay inside the card directory: {artifact.Path}",
                        nameof(workflow));
                }
            }
        }

        // Backlog is the list of cards not taken into work yet, so it is not a column a user may remove: it
        // has to exist and it has to be where a card enters the pipeline.
        var backlog = workflow.Stages.FirstOrDefault(WorkflowDefinition.IsBacklog)
            ?? throw new ArgumentException(
                $"A workflow must keep its {WorkflowDefinition.BacklogStageId} stage.",
                nameof(workflow));
        var firstOrder = workflow.Stages.Min(stage => stage.Order);
        if (backlog.Order != firstOrder)
        {
            throw new ArgumentException(
                $"The {WorkflowDefinition.BacklogStageId} stage must be the first stage of the workflow.",
                nameof(workflow));
        }
    }
}
