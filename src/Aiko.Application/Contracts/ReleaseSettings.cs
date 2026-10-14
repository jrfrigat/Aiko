namespace Aiko.Application.Contracts;

/// <summary>
/// Where a project's releases are published, and the schemes it holds for conducting them. An optional
/// section of <see cref="AppSettings"/>, written the way every other section is: silence means the safe
/// default, and a section stating only one half of the address is not configured.
/// </summary>
public sealed record ReleaseSettings(
    string? Owner = null,
    string? Repository = null,
    IReadOnlyList<ReleaseScheme>? Schemes = null)
{
    /// <summary>What a project that states nothing gets: no repository to probe, and both shipped schemes,
    /// which travel in the default so that a later improvement to one reaches every project, including the
    /// ones created before schemes existed.</summary>
    public static ReleaseSettings SafeDefault { get; } = new(Schemes: ReleaseSchemes.BuiltIn);

    /// <summary>True when both halves of the address are stated.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Owner) && !string.IsNullOrWhiteSpace(Repository);

    /// <summary>The repository as one address (<c>owner/repo</c>), or null when the section is incomplete.</summary>
    public string? Slug => IsConfigured ? $"{Owner!.Trim()}/{Repository!.Trim()}" : null;

    /// <summary>The schemes in force at this level: the ones it states, or the shipped ones when it states
    /// none (an empty list included). Which one a release follows is named where the release is asked for, so
    /// nothing falls back to an order nobody chose.</summary>
    public IReadOnlyList<ReleaseScheme> EffectiveSchemes =>
        Schemes is { Count: > 0 } stated ? stated : ReleaseSchemes.BuiltIn;
}
