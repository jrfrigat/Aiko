namespace Aiko.Application.Contracts;

/// <summary>
/// The rules a release record obeys: what it has to carry, and when it may not be written at all.
/// </summary>
/// <remarks>
/// The same reasoning as <c>CardCommands</c>: these are rules about releases rather than about one caller, so
/// they sit where the endpoint, the agent tool and the CLI read them from one place instead of each remembering
/// them. What a caller then does with a refusal stays its own decision.
/// </remarks>
public static class ReleaseRecords
{
    /// <summary>
    /// The prefix every release version carries, because a release version is a git tag.
    /// </summary>
    public const string VersionPrefix = "v";

    /// <summary>
    /// Rejects a release that names nothing a reader could find again.
    /// </summary>
    /// <param name="version">Version the release names.</param>
    /// <param name="schemeId">Scheme the release names.</param>
    /// <exception cref="ArgumentException">The version or the scheme is missing or unusable.</exception>
    public static void Validate(string? version, string? schemeId)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("A release must name its version.", nameof(version));
        }

        var trimmed = version.Trim();
        if (!trimmed.StartsWith(VersionPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"A release version is a git tag, so it starts with '{VersionPrefix}': '{trimmed}' is not one.",
                nameof(version));
        }

        if (string.IsNullOrWhiteSpace(schemeId))
        {
            throw new ArgumentException(
                "A release must name the scheme it followed, or it cannot say how it was conducted.",
                nameof(schemeId));
        }
    }

    /// <summary>
    /// Why the release may not be recorded, or null when it may be.
    /// </summary>
    /// <remarks>
    /// One version has one record. A second one would make "which cards went into v0.1.3" a question with two
    /// answers, and the whole point of the explicit list is that the question has one.
    /// </remarks>
    /// <param name="version">Version the caller wants to record.</param>
    /// <param name="existing">Releases already recorded.</param>
    public static string? RefuseRecord(string version, IReadOnlyList<ReleaseRecord> existing)
    {
        ArgumentNullException.ThrowIfNull(existing);
        return existing.FirstOrDefault(entry =>
            StringComparer.OrdinalIgnoreCase.Equals(entry.Version.Trim(), version?.Trim())) is { } recorded
            ? $"release '{recorded.Version}' is already recorded, at {recorded.ReleasedAt:u}. One version has " +
              $"one record: a second one would give \"which cards went into {recorded.Version}\" two answers."
            : null;
    }

    /// <summary>
    /// The card list as a record keeps it: trimmed, and without the blanks that name no card.
    /// </summary>
    /// <param name="cards">Cards the caller named, or null.</param>
    public static IReadOnlyList<string> NormalizeCards(IReadOnlyList<string>? cards) =>
        cards is null
            ? []
            : [.. cards.Where(card => !string.IsNullOrWhiteSpace(card)).Select(card => card.Trim())];
}
