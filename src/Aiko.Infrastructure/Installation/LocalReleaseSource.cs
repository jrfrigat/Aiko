using Aiko.Application.Installation;

namespace Aiko.Infrastructure.Installation;

/// <summary>
/// Releases laid out in a directory: a feed that needs no network.
/// </summary>
/// <remarks>
/// The layout is <c>&lt;root&gt;/latest.txt</c> holding the current tag, <c>&lt;root&gt;/&lt;tag&gt;/&lt;asset&gt;</c>
/// holding one release's assets, and <c>&lt;root&gt;/&lt;tag&gt;/SHA256SUMS</c> holding their checksums - the
/// same three things a GitHub release offers, in the same shape.
/// <para>
/// This is a production source and not a test double. The engine's acceptance criteria include "the
/// previous version is intact" and "the data and the projects are untouched", and those cannot be shown by
/// a run that needs to download from GitHub; a machine without internet or a mirror of the releases also
/// needs a way in.
/// </para>
/// </remarks>
public sealed class LocalReleaseSource : IReleaseSource
{
    /// <summary>File holding the tag the feed considers current.</summary>
    public const string LatestFileName = "latest.txt";

    private readonly string root;

    /// <summary>Creates a source over a directory.</summary>
    /// <param name="rootDirectory">The feed's root directory. It does not have to exist yet.</param>
    public LocalReleaseSource(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        root = Path.GetFullPath(rootDirectory);
    }

    /// <inheritdoc />
    public ValueTask<string> ResolveTagAsync(string? requestedTag, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(requestedTag) &&
            !string.Equals(requestedTag.Trim(), "latest", StringComparison.OrdinalIgnoreCase))
        {
            var explicitTag = requestedTag.Trim();
            if (!Directory.Exists(TagDirectory(explicitTag)))
            {
                throw new InvalidOperationException($"The local release feed has no release '{explicitTag}' in '{root}'.");
            }

            return ValueTask.FromResult(explicitTag);
        }

        var latestFile = Path.Combine(root, LatestFileName);
        if (!File.Exists(latestFile))
        {
            throw new InvalidOperationException(
                $"The local release feed has no '{LatestFileName}' in '{root}', so the current release is unknown.");
        }

        var tag = File.ReadAllText(latestFile).Trim();
        if (tag.Length == 0)
        {
            throw new InvalidOperationException($"'{latestFile}' is empty, so the current release is unknown.");
        }

        return ValueTask.FromResult(tag);
    }

    /// <inheritdoc />
    public ValueTask DownloadAssetAsync(
        string tag,
        string assetName,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(assetName);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var source = Path.Combine(TagDirectory(tag), assetName);
        if (!File.Exists(source))
        {
            throw new FileNotFoundException(
                $"The local release feed has no '{assetName}' for '{tag}' in '{root}'.",
                source);
        }

        File.Copy(source, destinationPath, overwrite: true);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyDictionary<string, string>> ReadChecksumsAsync(
        string tag,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var path = Path.Combine(TagDirectory(tag), ReleaseNaming.ChecksumsAssetName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"The local release feed has no '{ReleaseNaming.ChecksumsAssetName}' for '{tag}' in '{root}'.",
                path);
        }

        return ValueTask.FromResult(ChecksumFile.Parse(File.ReadAllText(path)));
    }

    private string TagDirectory(string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        // A tag names a directory here, so a tag that carries a separator or climbs out of the root would
        // read somebody else's files. Refused rather than escaped: no release tag contains either.
        var trimmed = tag.Trim();
        if (trimmed.Contains('/') ||
            trimmed.Contains('\\') ||
            trimmed.Contains("..", StringComparison.Ordinal))
        {
            throw new FormatException($"'{tag}' is not a release tag: it is not a single directory name.");
        }

        return Path.Combine(root, trimmed);
    }
}
