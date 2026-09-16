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
            cancellationToken);
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

    private static async ValueTask<IReadOnlyList<T>> ReadDocumentsAsync<T>(
        string directory,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
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

    private static async ValueTask WriteAtomicallyAsync(
        string path,
        WorkflowDefinition workflow,
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
                    workflow,
                    ProjectJsonContext.Default.WorkflowDefinition,
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
    }
}
