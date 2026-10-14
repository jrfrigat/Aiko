using Aiko.Domain.Cards;

namespace Aiko.Application.Contracts;

/// <summary>
/// Opens a card artifact in a program on the machine that runs the daemon, because the PWA is WebAssembly in
/// the user's browser and cannot start a local program at all. The daemon is loopback-only and the path is
/// resolved with the same confinement the artifact store uses, so an open can only ever reach inside a card's
/// own directory.
/// </summary>
public interface IArtifactOpener
{
    /// <summary>Opens the artifact at <paramref name="path"/>, relative to the card directory, with the
    /// program named by <paramref name="target"/>.</summary>
    ValueTask OpenAsync(
        CardReference card,
        string path,
        ArtifactOpenTarget target,
        CancellationToken cancellationToken);
}
