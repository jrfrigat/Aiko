using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
using Aiko.Infrastructure.Storage;

namespace Aiko.Infrastructure.Cards;

/// <summary>
/// Where a card's Markdown artifacts live, and how a relative path becomes a file path inside that folder.
/// </summary>
/// <remarks>
/// One place owns the confinement rules, so the store that reads and writes the documents and the opener
/// that hands a file to another program cannot disagree about which paths are allowed.
/// </remarks>
internal static class CardArtifactPaths
{
    /// <summary>
    /// The card's own directory, found beside the card where it actually is and confined to the project's
    /// data root.
    /// </summary>
    public static async ValueTask<string> FindCardDirectoryAsync(
        IProjectCatalog projects,
        ICardStore cards,
        CardReference reference,
        CancellationToken cancellationToken)
    {
        var project = await projects.FindAsync(reference.ProjectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Unknown Aiko project: {reference.ProjectId}");
        _ = await cards.FindAsync(reference, cancellationToken)
            ?? throw new KeyNotFoundException($"Unknown Aiko card: {reference.CardId}");
        // Beside the card where it actually is - not where its type's name says it should be, which after a
        // rename is another collection and would split the card's directory in two.
        var directory = FileCardStore.FindCardDirectory(project.RootPath, reference.CardId)
            ?? throw new KeyNotFoundException($"Unknown Aiko card: {reference.CardId}");
        // The check below the card directory covers the artifact's own path; this one covers the way to the
        // card directory itself, from .aiko down, where a linked collection would lead writes elsewhere.
        return PathConfinement.Resolve(AikoProjectPaths.DataRoot(project.RootPath), directory);
    }

    /// <summary>
    /// The full path of an artifact inside <paramref name="cardDirectory"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The path leaves the card directory or crosses a link.</exception>
    public static string ResolveArtifactPath(string cardDirectory, string path)
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

    /// <summary>
    /// A relative Markdown path with no traversal: the only shape an artifact path may take.
    /// </summary>
    public static string NormalizeRelativePath(string path)
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

    /// <summary>
    /// Refuses a path that crosses a link between the card directory and the artifact, which the full path
    /// alone cannot show.
    /// </summary>
    public static void EnsureNoReparsePoints(string cardDirectory, string artifactPath)
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
}
