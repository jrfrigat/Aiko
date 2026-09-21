namespace Aiko.Application.Installation;

/// <summary>
/// Files an installation keeps beside its own binaries.
/// </summary>
/// <remarks>
/// The directory is also on the user's <c>PATH</c>, so what lives in it is both Aiko's and the machine's:
/// the names here are what an uninstall, a repair and a version report all look for.
/// </remarks>
public static class InstallationFiles
{
    /// <summary>
    /// What is installed, and where it came from. Absent on an installation made before this file existed,
    /// which is a fact about the installation and not an error.
    /// </summary>
    public const string VersionFileName = "install.json";

    /// <summary>Directory holding <see cref="VersionFileName"/>, and the thing an uninstall removes.</summary>
    public const string BinDirectoryName = "bin";
}
