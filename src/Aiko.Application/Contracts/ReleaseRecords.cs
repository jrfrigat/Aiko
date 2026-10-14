namespace Aiko.Application.Contracts;

/// <summary>
/// The rules a release record obeys: what it has to carry, and when it may not be written at all. Stated once,
/// like the rules of <c>CardCommands</c>: the endpoint, the agent tool and the CLI read them from one place
/// instead of each remembering them, and what a caller then does with a refusal stays its own decision.
/// </summary>
public static class ReleaseRecords
{
    /// <summary>The prefix every release version carries, because a release version is a git tag.</summary>
    public const string VersionPrefix = "v";

    /// <summary>Rejects a release that names nothing a reader could find again.</summary>
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
    /// Why the release may not be recorded, or null when it may be. One version has one record: a second would
    /// make "which cards went into v0.1.3" a question with two answers, and the explicit list exists so that it
    /// has one.
    /// </summary>
    public static string? RefuseRecord(string version, IReadOnlyList<ReleaseRecord> existing)
    {
        ArgumentNullException.ThrowIfNull(existing);
        return existing.FirstOrDefault(entry =>
            StringComparer.OrdinalIgnoreCase.Equals(entry.Version.Trim(), version?.Trim())) is { } recorded
            ? $"release '{recorded.Version}' is already recorded, at {recorded.ReleasedAt:u}. One version has " +
              $"one record: a second one would give \"which cards went into {recorded.Version}\" two answers."
            : null;
    }

    /// <summary>The card list as a record keeps it: trimmed, and without the blanks that name no card.</summary>
    public static IReadOnlyList<string> NormalizeCards(IReadOnlyList<string>? cards) =>
        cards is null
            ? []
            : [.. cards.Where(card => !string.IsNullOrWhiteSpace(card)).Select(card => card.Trim())];
}
