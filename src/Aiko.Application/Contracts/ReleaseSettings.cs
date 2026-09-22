namespace Aiko.Application.Contracts;

/// <summary>
/// Where a project's releases are published, and the schemes it holds for conducting them.
/// </summary>
/// <remarks>
/// An optional section of <see cref="AppSettings"/>, written the way every other section is: silence means
/// the safe default, and the section is added to the document without changing its storage format.
/// <para>
/// Owner and repository are stated separately because they are the two halves of one address and GitHub
/// spells them apart; <see cref="Slug"/> joins them once, here, rather than in every screen that needs the
/// address. A section that states only one half is not configured - it is not a repository called
/// <c>owner/</c>, which GitHub would refuse anyway.
/// </para>
/// </remarks>
/// <param name="Owner">GitHub account the repository belongs to, for example <c>jrfrigat</c>.</param>
/// <param name="Repository">Repository name, for example <c>Aiko</c>.</param>
/// <param name="Schemes">
/// The schemes this level holds, or null to use the ones Aiko ships. Stating an empty list is the same as
/// stating none: a section that says nothing is not a section that offers nothing.
/// </param>
public sealed record ReleaseSettings(
    string? Owner = null,
    string? Repository = null,
    IReadOnlyList<ReleaseScheme>? Schemes = null)
{
    /// <summary>
    /// What a project that states nothing gets: no repository to probe, the ordinary scheme selected, and
    /// both shipped schemes available.
    /// </summary>
    /// <remarks>
    /// The schemes travel in the default on purpose. A project that states no release section - including one
    /// created before schemes existed - still has both of them, so they are visible without anyone editing a
    /// file, and a later improvement to a shipped scheme reaches every project instead of only the new ones.
    /// </remarks>
    public static ReleaseSettings SafeDefault { get; } = new(Schemes: ReleaseSchemes.BuiltIn);

    /// <summary>True when both halves of the address are stated.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Owner) && !string.IsNullOrWhiteSpace(Repository);

    /// <summary>
    /// The repository as one address (<c>owner/repo</c>), or null when the section is incomplete.
    /// </summary>
    public string? Slug => IsConfigured ? $"{Owner!.Trim()}/{Repository!.Trim()}" : null;

    /// <summary>
    /// The schemes in force at this level: the ones it states, or the shipped ones when it states none (and
    /// also when it states an empty list, which is a section that says nothing rather than one that offers
    /// nothing).
    /// </summary>
    /// <remarks>
    /// There is no scheme "in force" beyond this list. Which of them a release follows is named where that
    /// release is asked for - <c>/aiko-release &lt;scheme-id&gt;</c>, or the command a release dialog placed -
    /// so nothing here has to fall back to an order nobody chose, and a project may hold as many of them as
    /// it has kinds of release.
    /// </remarks>
    public IReadOnlyList<ReleaseScheme> EffectiveSchemes =>
        Schemes is { Count: > 0 } stated ? stated : ReleaseSchemes.BuiltIn;
}
