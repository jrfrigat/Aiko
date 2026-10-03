using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
using Aiko.Infrastructure.Cards;
using Aiko.Infrastructure.Storage;

namespace Aiko.Infrastructure.Memory;

/// <summary>
/// File-based queue of memory proposals: one JSON document per candidate, under
/// <c>&lt;card&gt;/memory-candidates/</c>, beside the card whose stage proposed it.
/// </summary>
/// <remarks>
/// The queue decides nothing. <see cref="ApproveAsync"/> is the one place in this store that writes the
/// durable memory, and it writes through <see cref="IMemoryStore"/>, so a proposal nobody approved can
/// never become knowledge. Paths are confined the way the artifact store confines them, so neither the
/// document's target nor a candidate's own file can leave the card's directory.
/// </remarks>
public sealed class FileMemoryCandidateStore(
    IProjectCatalog projects,
    ICardStore cards,
    IMemoryStore memory) : IMemoryCandidateStore
{
    /// <summary>Directory a card keeps its pending proposals in.</summary>
    private const string CandidateDirectoryName = "memory-candidates";

    /// <summary>Maximum proposed content length in characters, as for a stored document.</summary>
    private const int MaxContentLength = 2_000_000;

    /// <summary>The options a candidate document is written with: the context's own, with readable text.</summary>
    private static readonly JsonSerializerOptions DocumentOptions =
        new(MemoryCandidateJsonContext.Default.Options)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

    /// <inheritdoc />
    public async ValueTask<MemoryCandidate> ProposeAsync(
        string projectId,
        string cardId,
        string path,
        string content,
        string rationale,
        string author,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length > MaxContentLength)
        {
            throw new ArgumentException(
                $"Memory candidate exceeds the {MaxContentLength} character limit.",
                nameof(content));
        }

        var normalizedPath = NormalizeMemoryPath(path);
        var cardDirectory = await FindCardDirectoryAsync(projectId, cardId, cancellationToken);
        var candidate = new MemoryCandidate(
            $"mc_{Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant()}",
            cardId,
            normalizedPath,
            content,
            rationale ?? string.Empty,
            author ?? string.Empty,
            DateTimeOffset.UtcNow,
            MemoryCandidateStatus.Pending);
        await WriteAsync(cardDirectory, candidate, cancellationToken);
        return candidate;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<MemoryCandidate>> ListAsync(
        string projectId,
        string? cardId,
        CancellationToken cancellationToken)
    {
        var directories = new List<string>();
        if (!string.IsNullOrWhiteSpace(cardId))
        {
            directories.Add(await FindCardDirectoryAsync(projectId, cardId, cancellationToken));
        }
        else
        {
            var project = await EnsureProjectAsync(projectId, cancellationToken);
            foreach (var card in await cards.ListAsync(project.Id, cancellationToken))
            {
                // The whole project is the caller asking across every card; a card whose directory cannot be
                // found (a kind renamed since) is skipped rather than failing the listing.
                if (FileCardStore.FindCardDirectory(project.RootPath, card.Reference.CardId) is { } directory)
                {
                    directories.Add(PathConfinement.Resolve(
                        AikoProjectPaths.DataRoot(project.RootPath),
                        directory));
                }
            }
        }

        var candidates = new List<MemoryCandidate>();
        foreach (var directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var path in EnumerateCandidateFiles(directory))
            {
                if (await ReadAsync(path, cancellationToken) is { } candidate)
                {
                    candidates.Add(candidate);
                }
            }
        }

        return candidates
            .OrderByDescending(candidate => candidate.ProposedAt)
            .ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
            .ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<MemoryCandidate> ApproveAsync(
        string projectId,
        string cardId,
        string candidateId,
        string resolvedBy,
        string? decisionNote,
        CancellationToken cancellationToken)
    {
        var (cardDirectory, candidate) =
            await LoadPendingAsync(projectId, cardId, candidateId, cancellationToken);
        // The one write to the durable memory in this whole store.
        await memory.StoreAsync(projectId, candidate.Path, candidate.Content, cancellationToken);
        var approved = Resolve(candidate, MemoryCandidateStatus.Approved, resolvedBy, decisionNote);
        await WriteAsync(cardDirectory, approved, cancellationToken);
        return approved;
    }

    /// <inheritdoc />
    public async ValueTask<MemoryCandidate> RejectAsync(
        string projectId,
        string cardId,
        string candidateId,
        string resolvedBy,
        string? decisionNote,
        CancellationToken cancellationToken)
    {
        var (cardDirectory, candidate) =
            await LoadPendingAsync(projectId, cardId, candidateId, cancellationToken);
        // The memory is not touched: a rejected proposal is a decision, not knowledge.
        var rejected = Resolve(candidate, MemoryCandidateStatus.Rejected, resolvedBy, decisionNote);
        await WriteAsync(cardDirectory, rejected, cancellationToken);
        return rejected;
    }

    private static MemoryCandidate Resolve(
        MemoryCandidate candidate,
        MemoryCandidateStatus status,
        string resolvedBy,
        string? decisionNote) =>
        candidate with
        {
            Status = status,
            ResolvedAt = DateTimeOffset.UtcNow,
            ResolvedBy = resolvedBy ?? string.Empty,
            DecisionNote = string.IsNullOrWhiteSpace(decisionNote) ? null : decisionNote.Trim()
        };

    private async ValueTask<(string CardDirectory, MemoryCandidate Candidate)> LoadPendingAsync(
        string projectId,
        string cardId,
        string candidateId,
        CancellationToken cancellationToken)
    {
        var cardDirectory = await FindCardDirectoryAsync(projectId, cardId, cancellationToken);
        var path = CandidatePath(cardDirectory, candidateId);
        if (!File.Exists(path))
        {
            throw new KeyNotFoundException($"Unknown memory candidate: {candidateId}");
        }

        var candidate = await ReadAsync(path, cancellationToken)
            ?? throw new InvalidDataException($"Invalid memory candidate document: {path}");
        if (candidate.Status != MemoryCandidateStatus.Pending)
        {
            // A decision is not rewritten: a second approval would store the document twice over.
            throw new InvalidOperationException(
                $"Memory candidate '{candidate.Id}' was already {candidate.Status}.");
        }

        return (cardDirectory, candidate);
    }

    private static IEnumerable<string> EnumerateCandidateFiles(string cardDirectory)
    {
        var directory = Path.Combine(cardDirectory, CandidateDirectoryName);
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
            : [];
    }

    /// <summary>
    /// The full path of a candidate's own document, confined to the card's directory - a candidate id that
    /// climbs out is refused rather than followed.
    /// </summary>
    private static string CandidatePath(string cardDirectory, string candidateId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        var directory = Path.Combine(cardDirectory, CandidateDirectoryName);
        return PathConfinement.Resolve(cardDirectory, Path.Combine(directory, $"{candidateId}.json"));
    }

    /// <summary>
    /// A relative Markdown path with no traversal: the only shape a proposed memory document may take.
    /// </summary>
    private static string NormalizeMemoryPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = path.Replace('\\', '/').Trim('/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (Path.IsPathRooted(path) ||
            segments.Length == 0 ||
            segments.Any(segment => segment is "." or "..") ||
            !StringComparer.OrdinalIgnoreCase.Equals(Path.GetExtension(normalized), ".md"))
        {
            throw new ArgumentException(
                "Memory path must be a relative Markdown path.",
                nameof(path));
        }

        return string.Join('/', segments);
    }

    private async ValueTask<string> FindCardDirectoryAsync(
        string projectId,
        string cardId,
        CancellationToken cancellationToken)
    {
        var project = await EnsureProjectAsync(projectId, cancellationToken);
        _ = await cards.FindAsync(new CardReference(project.Id, cardId), cancellationToken)
            ?? throw new KeyNotFoundException($"Unknown Aiko card: {cardId}");
        var directory = FileCardStore.FindCardDirectory(project.RootPath, cardId)
            ?? throw new KeyNotFoundException($"Unknown Aiko card: {cardId}");
        return PathConfinement.Resolve(AikoProjectPaths.DataRoot(project.RootPath), directory);
    }

    private async ValueTask<RegisteredProject> EnsureProjectAsync(
        string projectId,
        CancellationToken cancellationToken) =>
        await projects.FindAsync(projectId, cancellationToken)
        ?? throw new KeyNotFoundException($"Unknown Aiko project: {projectId}");

    private static async ValueTask WriteAsync(
        string cardDirectory,
        MemoryCandidate candidate,
        CancellationToken cancellationToken)
    {
        var path = CandidatePath(cardDirectory, candidate.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(candidate, DocumentOptions);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                json,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
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

    private static async ValueTask<MemoryCandidate?> ReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync(
                stream,
                MemoryCandidateJsonContext.Default.MemoryCandidate,
                cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
