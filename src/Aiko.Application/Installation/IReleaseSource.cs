namespace Aiko.Application.Installation;

/// <summary>
/// Where a release comes from: which tag is current, the asset that carries it and the checksums that
/// vouch for it.
/// </summary>
/// <remarks>
/// The installation engine's <b>only</b> network seam, and an interface for a reason that is not testing
/// convenience: "the previous version is intact" and "the data and the projects are untouched" cannot be shown
/// by a run that needs GitHub, so a local feed is a second production implementation of this contract rather
/// than a test double.
/// </remarks>
public interface IReleaseSource
{
    /// <summary>
    /// Resolves the tag to install: the requested one, or - for null or <c>latest</c> - the current release.
    /// </summary>
    /// <exception cref="InvalidOperationException">The tag cannot be resolved.</exception>
    ValueTask<string> ResolveTagAsync(string? requestedTag, CancellationToken cancellationToken);

    /// <summary>Copies one asset of a release to a local path.</summary>
    /// <remarks>
    /// The asset name is the one <see cref="ReleaseNaming.AssetNameForTag"/> builds; the source overwrites
    /// <paramref name="destinationPath"/>.
    /// </remarks>
    ValueTask DownloadAssetAsync(
        string tag,
        string assetName,
        string destinationPath,
        CancellationToken cancellationToken);

    /// <summary>Reads the release's <c>SHA256SUMS</c>, as asset name to lowercase SHA-256.</summary>
    ValueTask<IReadOnlyDictionary<string, string>> ReadChecksumsAsync(
        string tag,
        CancellationToken cancellationToken);
}
