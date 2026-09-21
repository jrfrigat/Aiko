namespace Aiko.Infrastructure.Storage;

/// <summary>
/// The daemon's start-at-sign-in entry: one command file in the user's own Startup folder.
/// </summary>
/// <remarks>
/// <para>
/// A per-user startup folder rather than a registry Run key, for two reasons: the file is plain text the
/// person can read, and removing it is deleting a file - no registry hive, no separate API, nothing that
/// only Windows can do. A machine that has no such folder (every platform but Windows) reports itself as
/// not enabled and refuses to write, instead of failing halfway.
/// </para>
/// <para>
/// Nothing is written without being asked for: the installer offers the choice, defaults to not writing,
/// and <c>aiko uninstall</c> removes the entry as part of taking the installation off.
/// </para>
/// </remarks>
public sealed class AikoAutostart
{
    /// <summary>Name of the entry inside the Startup folder. Also what a person sees and deletes.</summary>
    public const string EntryFileName = "aiko-autostart.cmd";

    /// <summary>The command the entry starts: the daemon detached, with no console of its own to keep.</summary>
    public const string StartArguments = "serve --detached";

    /// <summary>
    /// Creates the entry for one installation.
    /// </summary>
    /// <param name="executablePath">The <c>aiko</c> command the entry starts.</param>
    /// <param name="startupDirectory">
    /// Folder the entry lives in; the user's own Startup folder when it is not stated, which is what a test
    /// needs to leave the machine's real one alone.
    /// </param>
    public AikoAutostart(string executablePath, string? startupDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ExecutablePath = executablePath;
        StartupDirectory = startupDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.Startup);
    }

    /// <summary>Folder the entry lives in, or an empty string on a platform that has none.</summary>
    public string StartupDirectory { get; }

    /// <summary>The executable the entry starts.</summary>
    public string ExecutablePath { get; }

    /// <summary>Full path of the entry file.</summary>
    public string EntryPath => Path.Combine(StartupDirectory, EntryFileName);

    /// <summary>Whether this machine has a per-user startup folder at all.</summary>
    public bool IsSupported => !string.IsNullOrWhiteSpace(StartupDirectory);

    /// <summary>Whether the daemon is set to start at sign-in.</summary>
    public bool IsEnabled => IsSupported && File.Exists(EntryPath);

    /// <summary>
    /// What the entry file says, or null when there is none. The body is stated as data rather than built by
    /// the writer alone, so what is written can be asserted without running it.
    /// </summary>
    /// <remarks>
    /// <c>start "" /min</c> is there because the command file runs in the user's session: the CLI starts the
    /// daemon and exits, and without the window handling that exit is visible on every sign-in.
    /// </remarks>
    public string Body =>
        string.Join(
            Environment.NewLine,
            [
                "@echo off",
                "rem Written by `aiko autostart enable`. Remove this file, or run `aiko autostart disable`,",
                "rem to stop the daemon starting at sign-in.",
                $"start \"\" /min \"{ExecutablePath}\" {StartArguments}"
            ]) + Environment.NewLine;

    /// <summary>
    /// Writes the entry, replacing one that is already there.
    /// </summary>
    public void Enable()
    {
        if (!IsSupported)
        {
            throw new InvalidOperationException(
                "This machine has no per-user startup folder, so the daemon cannot be made to start at " +
                "sign-in. Start it with `aiko serve --detached` instead.");
        }

        Directory.CreateDirectory(StartupDirectory);
        File.WriteAllText(EntryPath, Body);
    }

    /// <summary>
    /// Removes the entry.
    /// </summary>
    /// <returns>Whether there was one to remove.</returns>
    public bool Disable()
    {
        if (!IsEnabled)
        {
            return false;
        }

        File.Delete(EntryPath);
        return true;
    }
}
