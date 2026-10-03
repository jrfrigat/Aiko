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
        var cardDirectory = await CardArtifactPaths.FindCardDirectoryAsync(projects, cards, card, cancellationToken);
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
        var cardDirectory = await CardArtifactPaths.FindCardDirectoryAsync(projects, cards, card, cancellationToken);
        var artifactPath = CardArtifactPaths.ResolveArtifactPath(cardDirectory, path);
        CardArtifactPaths.EnsureNoReparsePoints(cardDirectory, artifactPath);
        if (!File.Exists(artifactPath))
        {
            return null;
        }

        var content = await File.ReadAllTextAsync(artifactPath, cancellationToken);
        var info = new FileInfo(artifactPath);
        return new CardArtifactDocument(
            CardArtifactPaths.NormalizeRelativePath(path),
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

        var cardDirectory = await CardArtifactPaths.FindCardDirectoryAsync(projects, cards, card, cancellationToken);
        var artifactPath = CardArtifactPaths.ResolveArtifactPath(cardDirectory, path);
        var lockKey = $"{card.ProjectId}/{card.CardId}/{CardArtifactPaths.NormalizeRelativePath(path)}";

        using (await locks.LockAsync(lockKey, cancellationToken))
        {
            CardArtifactPaths.EnsureNoReparsePoints(cardDirectory, artifactPath);
            var existingContent = File.Exists(artifactPath)
                ? await File.ReadAllTextAsync(artifactPath, cancellationToken)
                : null;
            var actualVersion = existingContent is null ? null : ComputeVersion(existingContent);
            if (!StringComparer.Ordinal.Equals(expectedVersion, actualVersion))
            {
                throw new ArtifactConflictException(path, expectedVersion, actualVersion);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
            CardArtifactPaths.EnsureNoReparsePoints(cardDirectory, artifactPath);
            await WriteAtomicallyAsync(artifactPath, content, cancellationToken);
            var info = new FileInfo(artifactPath);
            return new CardArtifactDocument(
                CardArtifactPaths.NormalizeRelativePath(path),
                content,
                ComputeVersion(content),
                info.LastWriteTimeUtc);
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
