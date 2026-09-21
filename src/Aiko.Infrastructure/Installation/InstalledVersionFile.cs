using System.Text.Json;
using Aiko.Application.Installation;

namespace Aiko.Infrastructure.Installation;

/// <summary>
/// The <c>install.json</c> beside the binaries: which release is installed, and where it came from.
/// </summary>
/// <remarks>
/// One type writes and reads it, so the file cannot come to mean two things. A missing file is a fact about
/// the installation - it was made before the file existed - and the reader says so by returning null rather
/// than inventing a version. A file that cannot be parsed is a different matter, and it throws: absent and
/// broken must not look alike, or a corrupt file would be reported as an installation of unknown age.
/// </remarks>
public static class InstalledVersionFile
{
    /// <summary>
    /// Reads it, or null when the installation predates the file.
    /// </summary>
    /// <param name="installDirectory">Directory the binaries live in.</param>
    public static InstalledVersion? Read(string installDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);

        var path = Path.Combine(installDirectory, InstallationFiles.VersionFileName);
        return File.Exists(path)
            ? JsonSerializer.Deserialize(File.ReadAllText(path), InstallationJsonContext.Default.InstalledVersion)
            : null;
    }

    /// <summary>
    /// Writes it, replacing any previous file in one step.
    /// </summary>
    /// <param name="installDirectory">Directory the binaries live in.</param>
    /// <param name="version">What to record.</param>
    public static InstalledVersion Write(string installDirectory, InstalledVersion version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);
        ArgumentNullException.ThrowIfNull(version);

        Directory.CreateDirectory(installDirectory);
        var path = Path.Combine(installDirectory, InstallationFiles.VersionFileName);
        var temporary = path + ".tmp";
        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(version, InstallationJsonContext.Default.InstalledVersion));

        // Moved into place rather than written in place: a run that died mid-write would otherwise leave a
        // truncated file, and every later reading of it would report corruption instead of a version.
        File.Move(temporary, path, overwrite: true);
        return version;
    }
}
