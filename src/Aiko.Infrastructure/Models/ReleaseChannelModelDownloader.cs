using Aiko.Application.Contracts;
using Aiko.Application.Installation;

namespace Aiko.Infrastructure.Models;

/// <summary>
/// Fetches a model's files from the Aiko release channel: the same path, and the same kind of asset, that
/// carries an update.
/// </summary>
/// <remarks>
/// The release is named by the registry rather than resolved as "latest", so a file that a model is made of
/// cannot change under an installation that already verified it. The release itself is asked for what it is -
/// the engine's own <see cref="IReleaseSource"/> - so a machine that mirrors releases gets the model from its
/// mirror as well, with no second mechanism to point somewhere else.
/// </remarks>
public sealed class ReleaseChannelModelDownloader : IEmbeddingModelDownloader
{
    private readonly IReleaseSource releases;

    /// <summary>Creates the downloader.</summary>
    /// <param name="releases">Release channel to fetch assets from; the caller owns it.</param>
    public ReleaseChannelModelDownloader(IReleaseSource releases)
    {
        ArgumentNullException.ThrowIfNull(releases);
        this.releases = releases;
    }

    /// <inheritdoc />
    public EmbeddingModelSource Source => EmbeddingModelSource.Release;

    /// <inheritdoc />
    public async ValueTask DownloadAsync(
        EmbeddingModel model,
        EmbeddingModelFile file,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(file);

        // Resolved first so that a release that does not exist is reported as that, before a request for an
        // asset inside it is made.
        var tag = await releases.ResolveTagAsync(model.ReleaseTag, cancellationToken);
        await releases.DownloadAssetAsync(tag, file.Name, destinationPath, cancellationToken);
    }
}
