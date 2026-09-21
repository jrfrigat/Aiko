using Aiko.Application.Installation;

namespace Aiko.Installer;

/// <summary>What the command line asked the installer to do.</summary>
internal enum InstallationVerb
{
    /// <summary>Put the resolved release in place, whether or not something is installed already.</summary>
    Install,

    /// <summary>Bring an existing installation up to the resolved release.</summary>
    Update,

    /// <summary>Report what is installed and what is available, and change nothing.</summary>
    Check,

    /// <summary>Print the installed version, or this build's version when nothing is installed.</summary>
    Version
}

/// <summary>
/// The installer's own command line, as data.
/// </summary>
/// <remarks>
/// Parsed here rather than by the engine, because the engine deliberately knows nothing about a command
/// line: it takes an <see cref="InstallationRequest"/> that is already a decision. What this type adds is
/// only the shape a person and the bootstrap script type - a verb and the flags of
/// <c>scripts/install.ps1</c> - and it is kept apart from the program so the rules are readable in one
/// place.
/// </remarks>
/// <param name="Verb">Which of the four things the run was asked for.</param>
/// <param name="InstallDirectory">Directory to install into, or null for the installation's own <c>bin</c>.</param>
/// <param name="Tag">Release tag the bootstrap resolved, or null to resolve the newest release.</param>
/// <param name="UpdatePath">Whether the directory may be added to the user <c>PATH</c> (<c>--no-path</c> says no).</param>
/// <param name="Agents">Adapter identifiers to connect, or null when the run named none.</param>
/// <param name="NoAgents">Whether the run asked for nobody to be connected.</param>
/// <param name="Force">Allow replacing a newer installation with an older one.</param>
/// <param name="AssumedYes">
/// Whether the run passed <c>--yes</c>. Recorded and not acted on: this program never prompts - it is
/// started by a script, where a question with no one to answer it is a hang - so the flag says what the
/// caller already decided rather than changing what happens here.
/// </param>
internal sealed record InstallerArguments(
    InstallationVerb Verb,
    string? InstallDirectory,
    string? Tag,
    bool UpdatePath,
    IReadOnlyList<string>? Agents,
    bool NoAgents,
    bool Force,
    bool AssumedYes)
{
    /// <summary>What the request the engine takes looks like, once the directory is known.</summary>
    public InstallationRequest ToInstallationRequest(string installDirectory) =>
        new(
            installDirectory,
            Tag,
            UpdatePath,
            Agents,
            NoAgents,
            Force,
            Check: Verb is InstallationVerb.Check);
}
