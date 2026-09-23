using System.Security.Cryptography;
using System.Text;
using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
using Aiko.Infrastructure.Threading;

namespace Aiko.Infrastructure.Cards;

/// <summary>
/// File-based store of card Markdown artifacts: paths strictly inside the card
/// directory, symlink-traversal protection and SHA-256 versioning for conflicts.
/// </summary>
public sealed class FileCardArtifactStore(
    IProjectCatalog projects,
    ICardStore cards) : ICardArtifactStore
{
    /// <summary>
    /// Maximum artifact content length in characters.
    /// </summary>
    private const int MaxContentLength = 2_000_000;

    /// <summary>
    /// Reference-counted per-artifact locks; idle keys are dropped automatically.
    /// </summary>
    private readonly KeyedLockStore locks = new();

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<CardArtifactSummary>> ListAsync(
        CardReference card,
        CancellationToken cancellationToken)
    {
        var cardDirectory = await FindCardDirectoryAsync(card, cancellationToken);
        if (!Directory.Exists(cardDirectory))
        {
            return [];
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        return Directory.EnumerateFiles(cardDirectory, "*.md", options)
            .Select(path =>
            {
                var info = new FileInfo(path);
                return new CardArtifactSummary(
                    Path.GetRelativePath(cardDirectory, path).Replace('\\', '/'),
                    info.Length,
                    info.LastWriteTimeUtc);
            })
            .OrderBy(document => document.Path, StringComparer.Ordinal)
            .ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<CardArtifactDocument?> ReadAsync(
        CardReference card,
        string path,
        CancellationToken cancellationToken)
    {
        var cardDirectory = await FindCardDirectoryAsync(card, cancellationToken);
        var artifactPath = ResolveArtifactPath(cardDirectory, path);
        EnsureNoReparsePoints(cardDirectory, artifactPath);
        if (!File.Exists(artifactPath))
        {
            return null;
        }

        var content = await File.ReadAllTextAsync(artifactPath, cancellationToken);
        var info = new FileInfo(artifactPath);
        return new CardArtifactDocument(
            NormalizeRelativePath(path),
            content,
            ComputeVersion(content),
            info.LastWriteTimeUtc);
    }

    /// <inheritdoc />
    public async ValueTask<CardArtifactDocument> SaveAsync(
        CardReference card,
        string path,
        string content,
        string? expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length > MaxContentLength)
        {
            throw new ArgumentException(
                $"Markdown artifact exceeds the {MaxContentLength} character limit.",
                nameof(content));
        }

        var cardDirectory = await FindCardDirectoryAsync(card, cancellationToken);
        var artifactPath = ResolveArtifactPath(cardDirectory, path);
        var lockKey = $"{card.ProjectId}/{card.CardId}/{NormalizeRelativePath(path)}";

        using (await locks.LockAsync(lockKey, cancellationToken))
        {
            EnsureNoReparsePoints(cardDirectory, artifactPath);
            var existingContent = File.Exists(artifactPath)
                ? await File.ReadAllTextAsync(artifactPath, cancellationToken)
                : null;
            var actualVersion = existingContent is null ? null : ComputeVersion(existingContent);
            if (!StringComparer.Ordinal.Equals(expectedVersion, actualVersion))
            {
                throw new ArtifactConflictException(path, expectedVersion, actualVersion);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
            EnsureNoReparsePoints(cardDirectory, artifactPath);
            await WriteAtomicallyAsync(artifactPath, content, cancellationToken);
            var info = new FileInfo(artifactPath);
            return new CardArtifactDocument(
                NormalizeRelativePath(path),
                content,
                ComputeVersion(content),
                info.LastWriteTimeUtc);
        }
    }

    private async ValueTask<string> FindCardDirectoryAsync(
        CardReference reference,
        CancellationToken cancellationToken)
    {
        var project = await projects.FindAsync(reference.ProjectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Unknown Aiko project: {reference.ProjectId}");
        _ = await cards.FindAsync(reference, cancellationToken)
            ?? throw new KeyNotFoundException($"Unknown Aiko card: {reference.CardId}");
        // Beside the card where it actually is - not where its type's name says it should be, which after a
        // rename is another collection and would split the card's directory in two.
        return FileCardStore.FindCardDirectory(project.RootPath, reference.CardId)
            ?? throw new KeyNotFoundException($"Unknown Aiko card: {reference.CardId}");
    }

    private static string ResolveArtifactPath(string cardDirectory, string path)
    {
        var normalized = NormalizeRelativePath(path);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(cardDirectory));
        var target = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = root + Path.DirectorySeparatorChar;
        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Artifact path must stay inside the card directory.", nameof(path));
        }

        return target;
    }

    private static string NormalizeRelativePath(string path)
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
                "Artifact path must be a relative Markdown path inside the card directory.",
                nameof(path));
        }

        return string.Join('/', segments);
    }

    private static void EnsureNoReparsePoints(string cardDirectory, string artifactPath)
    {
        var relative = Path.GetRelativePath(cardDirectory, artifactPath);
        var current = cardDirectory;
        foreach (var segment in relative.Split(
                     Path.DirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if ((File.Exists(current) || Directory.Exists(current)) &&
                File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new IOException($"Artifact path crosses a reparse point: {relative}");
            }
        }
    }

    private static async ValueTask WriteAtomicallyAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                content,
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

    private static string ComputeVersion(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
}
