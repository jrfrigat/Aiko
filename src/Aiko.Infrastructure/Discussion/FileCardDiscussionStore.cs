using System.Text.Json;
using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
using Aiko.Infrastructure.Cards;
using Aiko.Infrastructure.Projects;
using Aiko.Infrastructure.Storage;

namespace Aiko.Infrastructure.Discussion;

/// <summary>
/// The notes on a card, kept in the card's own folder.
/// </summary>
/// <remarks>
/// One document per card, written atomically under the card's own lock: a note is something a person wrote,
/// so it lives where the card lives and survives anything happening to the database. The daemon is not the
/// only place it exists, which is the point.
/// </remarks>
public sealed class FileCardDiscussionStore(IProjectCatalog projects, ICardStore cards) : ICardDiscussionStore
{
    private const string FileName = "discussion.json";
    private const int CurrentSchemaVersion = 1;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<CardComment>> ListAsync(
        CardReference card,
        CancellationToken cancellationToken) =>
        (await ReadDocumentAsync(card, cancellationToken)).Entries;

    /// <inheritdoc />
    public async ValueTask<CardComment> AppendAsync(
        CardReference card,
        string author,
        string body,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        var trimmed = body.Trim();
        var comment = new CardComment(
            Guid.CreateVersion7().ToString("N"),
            string.IsNullOrWhiteSpace(author) ? "you" : author.Trim(),
            trimmed,
            DateTimeOffset.UtcNow);

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var document = await ReadDocumentAsync(card, cancellationToken);
            var updated = new DiscussionDocument(
                CurrentSchemaVersion,
                [.. document.Entries, comment]);
            await WriteDocumentAsync(card, updated, cancellationToken);
            return comment;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async ValueTask<DiscussionDocument> ReadDocumentAsync(
        CardReference card,
        CancellationToken cancellationToken)
    {
        var path = await ResolvePathAsync(card, cancellationToken);
        if (!File.Exists(path))
        {
            return new DiscussionDocument(CurrentSchemaVersion, []);
        }

        await using var input = File.OpenRead(path);
        var document = await JsonSerializer.DeserializeAsync(
            input,
            ProjectJsonContext.Default.DiscussionDocument,
            cancellationToken);
        return document ?? new DiscussionDocument(CurrentSchemaVersion, []);
    }

    private async ValueTask WriteDocumentAsync(
        CardReference card,
        DiscussionDocument document,
        CancellationToken cancellationToken)
    {
        var path = await ResolvePathAsync(card, cancellationToken);
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
                    ProjectJsonContext.Default.DiscussionDocument,
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

    /// <summary>
    /// The card's own <c>discussion.json</c>: the folder the card store files the card in, so the notes sit
    /// beside the card they belong to.
    /// </summary>
    private async ValueTask<string> ResolvePathAsync(CardReference card, CancellationToken cancellationToken)
    {
        var project = await FindProjectAsync(card.ProjectId, cancellationToken);
        var existing = await cards.FindAsync(card, cancellationToken);
        var collection = existing is null
            ? "tasks"
            : FileCardStore.CollectionFor(existing.Kind);
        var directory = Path.Combine(AikoProjectPaths.DataRoot(project.RootPath), collection, card.CardId);
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, FileName);
    }

    private async ValueTask<RegisteredProject> FindProjectAsync(string projectId, CancellationToken cancellationToken) =>
        await projects.FindAsync(projectId, cancellationToken)
        ?? throw new FileNotFoundException($"No Aiko project '{projectId}' is registered.");
}
