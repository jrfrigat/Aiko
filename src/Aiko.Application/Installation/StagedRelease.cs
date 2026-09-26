namespace Aiko.Application.Installation;

/// <summary>
/// A release downloaded, verified and unpacked, ready to replace what is installed.
/// </summary>
/// <remarks>
/// A contract rather than a private step of the downloader, because it now has two producers. The engine
/// fetches a release itself; on a machine with neither Aiko nor .NET the bootstrap script fetches and unpacks
/// it, and the freshly unpacked CLI finishes the job. <see cref="IInstallationEngine.FinishAsync"/> is what
/// takes the result, so the record travels with the interface rather than inside one implementation of it.
/// </remarks>
/// <param name="Tag">Release tag it came from.</param>
/// <param name="Version">Version the tag names.</param>
/// <param name="AssetName">Asset it was unpacked from.</param>
/// <param name="Sha256">
/// The verified checksum of that asset, or empty when the caller verified it and does not carry the value.
/// The replacement reads the tag, the version and the directory; the checksum is reported, not re-checked.
/// </param>
/// <param name="Directory">The staging directory holding the unpacked release.</param>
public sealed record StagedRelease(
    string Tag,
    string Version,
    string AssetName,
    string Sha256,
    string Directory);
