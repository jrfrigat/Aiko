namespace Aiko.Application.Contracts;

/// <summary>
/// A named instruction to an agent: how a release of this project is conducted.
/// </summary>
/// <remarks>
/// A document with a name, a description and a body - the order of steps. Not code and not a pipeline: the
/// build pipeline stays the single <c>release.yml</c>, and there are as many schemes as a project needs. A
/// scheme answers "what to do", not "what to build with".
/// <para>
/// The body is instruction text an agent reads, so it is written in English like every other agent-facing
/// text Aiko ships. Name and description are the owner's data rather than interface captions, which is why
/// they are not localized: a scheme the owner writes is shown as it was written.
/// </para>
/// </remarks>
/// <param name="Id">Stable identifier, also the value a project's settings name in <see cref="ReleaseSettings.Scheme"/>.</param>
/// <param name="Name">Display name.</param>
/// <param name="Description">What this scheme is for, shown where a scheme is chosen.</param>
/// <param name="Body">The steps, as the agent reads them.</param>
public sealed record ReleaseScheme(string Id, string Name, string Description, string Body);

/// <summary>
/// The schemes Aiko ships with, and the vocabulary around them.
/// </summary>
public static class ReleaseSchemes
{
    /// <summary>Identifier of the ordinary release scheme.</summary>
    public const string GitReleaseId = "git-release";

    /// <summary>Identifier of the preliminary release scheme.</summary>
    public const string GitPreReleaseId = "git-pre-release";

    /// <summary>
    /// The suffix that makes a tag a preliminary release - <c>v0.1.2-pre</c> rather than <c>v0.1.2</c>.
    /// </summary>
    /// <remarks>
    /// Named once, here, because three things read it: the scheme's body, the workflow that decides whether
    /// a release becomes the latest one, and the screen that marks a release as preliminary or ordinary.
    /// </remarks>
    public const string PreReleaseSuffix = "-pre";

    /// <summary>The two schemes every project has without stating any.</summary>
    public static IReadOnlyList<ReleaseScheme> BuiltIn { get; } =
        [GitRelease(), GitPreRelease()];

    /// <summary>
    /// The scheme an ordinary release follows: the tag names the version, and the release becomes the latest
    /// one, so a plain install picks it up.
    /// </summary>
    public static ReleaseScheme GitRelease() => new(
        GitReleaseId,
        "Git release",
        "An ordinary release: the tag carries the version, and the release becomes the latest one.",
        """
        1. Make sure the tree is green: `dotnet build Aiko.slnx -c Release` with no warnings, and
           `dotnet test Aiko.slnx -c Release` - every suite passes.
        2. Choose the version and explain the choice to the person: a patch for fixes only, a minor for a new
           capability, a major only when the person decides it.
        3. Give the person the tag command. The version is a tag of the shape `v<major>.<minor>.<patch>`,
           so the command is `git tag v0.1.3`. Do not push it yourself.
        4. Wait for the person to push the tag and for the release workflow to build the archive.
        5. Tell the person to install the release: `scripts/install.ps1 -Version v0.1.3`, or without
           `-Version` once this release is the latest one.
        6. Reconnect the agents: `aiko agent install --project <id>`.
        7. Record the release: `aiko_record_release(version, schemeId: "git-release", cards)`.

        The result: this release becomes the latest one, so an install without a version gives exactly it.
        """);

    /// <summary>
    /// The scheme a preliminary release follows: the same order, a tag carrying <c>-pre</c>, and a release
    /// that does not become the latest one.
    /// </summary>
    public static ReleaseScheme GitPreRelease() => new(
        GitPreReleaseId,
        "Git pre-release",
        "A preliminary release: the tag carries -pre, and only an explicit install gets it.",
        """
        1. Make sure the tree is green, exactly as for an ordinary release: the build with no warnings and
           every test suite passing.
        2. Choose the version and explain the choice to the person. A preliminary release is still a version,
           so the same rule applies: a patch for fixes, a minor for a capability.
        3. Give the person the tag command, with the suffix: the version is a tag of the shape
           `v<major>.<minor>.<patch>-pre`, so the command is `git tag v0.1.3-pre`. Do not push it yourself.
        4. Wait for the person to push the tag and for the release workflow to build the archive. The
           workflow recognises the `-pre` suffix and publishes the release as a preliminary one, so it does
           not become the latest release - a plain install keeps giving the previous ordinary release.
        5. Tell the person to install it by naming the version: `scripts/install.ps1 -Version v0.1.3-pre`.
           Without `-Version` this release is never installed, which is the point of it.
        6. Reconnect the agents: `aiko agent install --project <id>`.
        7. Record the release: `aiko_record_release(version, schemeId: "git-pre-release", cards)`.
        """);
}
