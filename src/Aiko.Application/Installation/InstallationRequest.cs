namespace Aiko.Application.Installation;

/// <summary>
/// What an install, an update or a report-only run was asked to do.
/// </summary>
/// <remarks>
/// One type carries the notions of every verb, because install and update are two names of one path: a
/// second request type would be a second place where "which directory, which tag, which agents" is decided,
/// and the two would drift. <see cref="Check"/> is a field rather than a method for the same reason - one
/// execution path with different behaviour, not two paths.
/// </remarks>
/// <param name="InstallDirectory">Directory the product is installed into and replaced inside.</param>
/// <param name="Tag">Explicit release tag, or null for the newest release.</param>
/// <param name="UpdatePath">Whether the directory may be written to the user <c>PATH</c>. False is <c>--no-path</c>.</param>
/// <param name="Agents">
/// Agent identifiers to connect after the install, or null to keep what is already configured.
/// </param>
/// <param name="NoAgents">
/// Connect nobody. Separate from an empty <paramref name="Agents"/> on purpose: "do not touch the agents"
/// and "connect none of them" are different instructions, and the install script already distinguishes them.
/// </param>
/// <param name="Force">Allow replacing a newer installation with an older one.</param>
/// <param name="Check">Report what is installed and what is available, and change nothing.</param>
public sealed record InstallationRequest(
    string InstallDirectory,
    string? Tag = null,
    bool UpdatePath = true,
    IReadOnlyList<string>? Agents = null,
    bool NoAgents = false,
    bool Force = false,
    bool Check = false);
