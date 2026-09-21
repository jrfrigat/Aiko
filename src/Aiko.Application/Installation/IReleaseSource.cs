namespace Aiko.Application.Installation;

/// <summary>
/// Where a release comes from: which tag is current, the asset that carries it and the checksums that
/// vouch for it.
/// </summary>
/// <remarks>
/// This is the installation engine's <b>only</b> network seam, and it exists as an interface for a reason
/// that is not testing convenience: the acceptance criteria of the story that asked for this engine include
/// "the previous version is intact" and "the data and the projects are untouched", and neither can be shown
/// by a run that needs GitHub. A local feed is therefore a second production implementation of this same
/// contract, not a test double.
/// </remarks>
public interface IReleaseSource
{
    /// <summary>
    /// Resolves the tag to install: the requested one, or the current release when none is named.
    /// </summary>
    /// <param name="requestedTag">Explicit tag, or null or <c>latest</c> for the newest release.</param>
    /// <param name="cancellationToken">Cancellation token for the resolution.</param>
    /// <exception cref="InvalidOperationException">The tag cannot be resolved.</exception>
    ValueTask<string> ResolveTagAsync(string? requestedTag, CancellationToken cancellationToken);

    /// <summary>Copies one asset of a release to a local path.</summary>
    /// <param name="tag">Resolved release tag.</param>
    /// <param name="assetName">Asset file name, as <see cref="ReleaseNaming.AssetNameForTag"/> builds it.</param>
    /// <param name="destinationPath">Where to write it. The source overwrites it.</param>
    /// <param name="cancellationToken">Cancellation token for the download.</param>
    ValueTask DownloadAssetAsync(
        string tag,
        string assetName,
        string destinationPath,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads the release's <c>SHA256SUMS</c>, as asset name to lowercase SHA-256.
    /// </summary>
    /// <param name="tag">Resolved release tag.</param>
    /// <param name="cancellationToken">Cancellation token for the download.</param>
    ValueTask<IReadOnlyDictionary<string, string>> ReadChecksumsAsync(
        string tag,
        CancellationToken cancellationToken);
}
