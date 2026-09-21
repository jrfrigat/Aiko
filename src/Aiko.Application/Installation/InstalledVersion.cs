namespace Aiko.Application.Installation;

/// <summary>
/// Which release is installed, and where: the record written beside the binaries as <c>install.json</c>.
/// </summary>
/// <remarks>
/// One type for writing and reading it, so the file cannot come to mean two things. A missing file is not an
/// error - it means the installation was made before the file existed, and the version is then read from the
/// binary rather than invented.
/// </remarks>
/// <param name="Tag">Release tag as published, for example <c>v0.9.2</c>.</param>
/// <param name="Version">Version without the tag's leading <c>v</c>, for example <c>0.9.2</c>.</param>
/// <param name="Channel">
/// Release channel. Fixed to <c>latest</c> for now: there are no channels yet, and the field is here so the
/// record does not have to change shape when there are.
/// </param>
/// <param name="InstalledAtUtc">When this version was put in place.</param>
/// <param name="InstallDirectory">The directory it was put in.</param>
public sealed record InstalledVersion(
    string Tag,
    string Version,
    string Channel,
    DateTimeOffset InstalledAtUtc,
    string InstallDirectory)
{
    /// <summary>The channel every installation currently comes from.</summary>
    public const string LatestChannel = "latest";
}
