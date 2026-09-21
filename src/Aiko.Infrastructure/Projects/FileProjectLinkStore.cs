using System.Text.Json;
using Aiko.Application.Contracts;
using Aiko.Infrastructure.Storage;
using Aiko.Infrastructure.Threading;

namespace Aiko.Infrastructure.Projects;

/// <summary>
/// File-based store of linked projects: <c>.aiko/links.json</c> as the source of truth, with the targets
/// resolved against the registry of projects.
/// </summary>
/// <remarks>
/// A link is written, never merged: the file holds a short list a person reads, so a wrong entry is easier to
/// see and to remove than a silently reconciled one. The target's immutable id is what is stored - the handle
/// travels beside it for display and for the skill the user types, and a project that was renamed keeps its
/// links.
/// </remarks>
public sealed class FileProjectLinkStore(IProjectCatalog projects) : IProjectLinkStore
{
    /// <summary>Reference-counted per-project locks; idle keys are dropped automatically.</summary>
    private readonly KeyedLockStore locks = new();

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ProjectLink>> ListAsync(
        string projectId,
        CancellationToken cancellationToken)
    {
        var project = await FindProjectAsync(projectId, cancellationToken);
        return (await ReadDocumentAsync(project.RootPath, cancellationToken)).Links;
    }

    /// <inheritdoc />
    public async ValueTask<ProjectLink> SaveAsync(
        string projectId,
        string targetProjectId,
        ProjectLinkText text,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetProjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(text.Description);

        var project = await FindProjectAsync(projectId, cancellationToken);
        var target = await FindProjectAsync(targetProjectId, cancellationToken);
        if (StringComparer.Ordinal.Equals(project.Id, target.Id))
        {
            throw new InvalidOperationException("A project cannot be linked to itself.");
        }

        var link = new ProjectLink(
            target.Id,
            target.Handle,
            text.Description.Trim(),
            DateTimeOffset.UtcNow,
            Normalize(text.Reference),
            Normalize(text.WhenToUse),
            Normalize(text.WhenNotToUse));
        using (await locks.LockAsync(project.Id, cancellationToken))
        {
            var document = await ReadDocumentAsync(project.RootPath, cancellationToken);
            var links = document.Links
                .Where(existing => !StringComparer.Ordinal.Equals(existing.ProjectId, link.ProjectId))
                .ToList();
            links.Add(link);
            await WriteDocumentAsync(
                project.RootPath,
                document with { Revision = document.Revision + 1, Links = links },
                cancellationToken);
        }

        return link;
    }

    /// <inheritdoc />
    public async ValueTask RemoveAsync(
        string projectId,
        string targetProjectId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetProjectId);
        var project = await FindProjectAsync(projectId, cancellationToken);
        var target = await FindProjectAsync(targetProjectId, cancellationToken);

        using (await locks.LockAsync(project.Id, cancellationToken))
        {
            var document = await ReadDocumentAsync(project.RootPath, cancellationToken);
            var links = document.Links
                .Where(existing => !StringComparer.Ordinal.Equals(existing.ProjectId, target.Id))
                .ToList();
            if (links.Count == document.Links.Count)
            {
                return;
            }

            await WriteDocumentAsync(
                project.RootPath,
                document with { Revision = document.Revision + 1, Links = links },
                cancellationToken);
        }
    }

    private async ValueTask<RegisteredProject> FindProjectAsync(
        string projectId,
        CancellationToken cancellationToken) =>
        await projects.FindAsync(projectId, cancellationToken)
        ?? throw new KeyNotFoundException($"Unknown Aiko project: {projectId}");

    /// <summary>
    /// One of a link's optional texts, trimmed, with blank turned into null: "nobody said" then has a single
    /// representation, because an empty string and an absent field would print differently and read alike.
    /// </summary>
    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static async ValueTask<ProjectLinkDocument> ReadDocumentAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(AikoProjectPaths.DataRoot(projectRoot), "links.json");
        if (!File.Exists(path))
        {
            return new ProjectLinkDocument(1, 0, []);
        }

        await using var input = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ProjectLinkDocument>(
                input,
                AikoJson.Project,
                cancellationToken)
            ?? throw new InvalidDataException($"Invalid link document: {path}");
    }

    private static async ValueTask WriteDocumentAsync(
        string projectRoot,
        ProjectLinkDocument document,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(AikoProjectPaths.DataRoot(projectRoot), "links.json");
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
                await JsonSerializer.SerializeAsync(output, document, AikoJson.Project, cancellationToken);
            }

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
}
