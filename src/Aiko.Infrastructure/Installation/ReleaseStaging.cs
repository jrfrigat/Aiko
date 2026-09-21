using Aiko.Application.Installation;
using Aiko.Infrastructure.Storage;

namespace Aiko.Infrastructure.Installation;

/// <summary>
/// A release downloaded, verified and unpacked, ready to replace what is installed.
/// </summary>
/// <param name="Tag">Release tag it came from.</param>
/// <param name="Version">Version the tag names.</param>
/// <param name="AssetName">Asset it was unpacked from.</param>
/// <param name="Sha256">The verified checksum of that asset.</param>
/// <param name="Directory">The staging directory holding the unpacked release.</param>
public sealed record StagedRelease(string Tag, string Version, string AssetName, string Sha256, string Directory);

/// <summary>
/// Prepares a release beside the installation: download, verify, unpack, validate.
/// </summary>
/// <remarks>
/// Everything happens next to the install directory, never inside it, and the order is the guarantee the
/// story asks for: the checksum is verified and the layout is validated <b>before</b> anything replaces the
/// installed version, so a download that failed, a substituted archive or an incomplete release leaves the
/// previous version exactly as it was - without a separate rollback path for it.
/// <para>
/// The unpacking itself is not done here: <see cref="ProjectArchive.ExtractAsync"/> already unpacks and
/// refuses any entry that would land outside its target, and a second unpacker would be a second place where
/// that protection lives.
/// </para>
/// </remarks>
public sealed class ReleaseStaging(IReleaseSource source)
{
    /// <summary>
    /// Downloads the release, verifies it against its published checksum and unpacks it into a staging
    /// directory beside the installation.
    /// </summary>
    /// <param name="tag">Resolved release tag.</param>
    /// <param name="installDirectory">Directory the release is meant for; it is not touched.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InstallationRefusedException">The release does not vouch for the asset, or the asset is not a complete release.</exception>
    public async ValueTask<StagedRelease> PrepareAsync(
        string tag,
        string installDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);

        var assetName = ReleaseNaming.AssetNameForTag(tag);
        var version = ReleaseNaming.VersionFromTag(tag);

        // The checksums come first. A release that does not vouch for the archive is refused before it is
        // downloaded: cheaper, and the honest order, because the checksum is the reason to download at all.
        var checksums = await source.ReadChecksumsAsync(tag, cancellationToken);
        if (!checksums.TryGetValue(assetName, out var expected))
        {
            throw new InstallationRefusedException(
                $"Release {tag} publishes no checksum for {assetName}, so the download cannot be verified.");
        }

        var stagingDirectory = $"{installDirectory}.staging-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
        var archivePath = stagingDirectory + ".zip";

        try
        {
            await source.DownloadAssetAsync(tag, assetName, archivePath, cancellationToken);

            var actual = await FileHash.Sha256Async(archivePath, cancellationToken);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InstallationRefusedException(
                    $"{assetName} does not match its published checksum (expected {expected}, and the download is {actual}). " +
                    "Nothing was installed.");
            }

            // A staging directory left by an earlier attempt is discarded rather than unpacked into: mixing
            // two attempts would make "what is staged" unanswerable.
            Discard(stagingDirectory);
            await ProjectArchive.ExtractAsync(archivePath, stagingDirectory, cancellationToken);

            var missing = MissingEntries(stagingDirectory);
            if (missing.Count > 0)
            {
                throw new InstallationRefusedException(
                    $"{assetName} is not a complete Aiko release: it carries no {string.Join(", no ", missing)}.");
            }

            return new StagedRelease(tag, version, assetName, actual, stagingDirectory);
        }
        catch
        {
            // A failed attempt leaves nothing beside the installation: the next run must not find half a
            // staging directory and have to guess what it was.
            Discard(stagingDirectory);
            throw;
        }
        finally
        {
            Discard(archivePath);
        }
    }

    private static IReadOnlyList<string> MissingEntries(string directory) =>
        ReleaseLayout.RequiredEntries
            .Where(entry => !File.Exists(Path.Combine(directory, entry)))
            .ToArray();

    private static void Discard(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Whatever is left is a stray file beside the installation. Failing here would replace the real
            // reason the run stopped with a complaint about cleaning up after it.
        }
    }
}
