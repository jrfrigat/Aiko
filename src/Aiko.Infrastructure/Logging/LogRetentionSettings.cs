namespace Aiko.Infrastructure.Logging;

/// <summary>
/// How much of its own record the daemon keeps: the log file it writes in the background, and the
/// append-only event journal that records what happened to every card.
/// </summary>
/// <remarks>
/// Both bounds exist because the alternative is a machine's own data directory growing without limit. They
/// are settings rather than constants for the same reason the port is: how much history is worth keeping is
/// the installation's decision, and a value compiled in can only be changed by rebuilding.
/// <para>
/// Trimming is never silent. A log file that is dropped is named by the file that replaces it, and journal
/// rows that are removed leave a marker event behind, so "where did yesterday's log go?" has an answer in
/// the product rather than in a guess about what deleted it.
/// </para>
/// </remarks>
/// <param name="MaxFileBytes">Size the daemon log reaches before it is rotated to the next numbered file.</param>
/// <param name="MaxFiles">How many daemon log files are kept in total, the current one included.</param>
/// <param name="JournalMaxAgeDays">Age past which event journal rows are trimmed.</param>
public sealed record LogRetentionSettings(
    int MaxFileBytes = LogRetentionSettings.DefaultMaxFileBytes,
    int MaxFiles = LogRetentionSettings.DefaultMaxFiles,
    int JournalMaxAgeDays = LogRetentionSettings.DefaultJournalMaxAgeDays)
{
    /// <summary>
    /// Size at which the daemon log is rotated: the value the daemon rotated at before this setting existed.
    /// </summary>
    public const int DefaultMaxFileBytes = 8 * 1024 * 1024;

    /// <summary>Number of daemon log files kept by default: the current one and four rotated ones.</summary>
    public const int DefaultMaxFiles = 5;

    /// <summary>Age past which journal rows are trimmed by default.</summary>
    public const int DefaultJournalMaxAgeDays = 30;

    /// <summary>
    /// Smallest size that still rotates usefully: a file capped below this would be rotated faster than a
    /// person could read it, which turns a log into a list of its own rotations.
    /// </summary>
    public const int MinimumFileBytes = 64 * 1024;

    /// <summary>
    /// Fewest files a rotation may keep. One file is not a rotation at all - it is the file overwritten in
    /// place, which is exactly the silent loss this setting exists to prevent.
    /// </summary>
    public const int MinimumFiles = 2;

    /// <summary>Shortest retention: shorter than this and the journal outlives nothing it records.</summary>
    public const int MinimumJournalDays = 1;

    /// <summary>The settings an installation that has recorded none uses.</summary>
    public static LogRetentionSettings Default { get; } = new();

    /// <summary>
    /// Throws when a bound would make the daemon lose its own record faster than it can write it.
    /// </summary>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxFileBytes, MinimumFileBytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxFiles, MinimumFiles);
        ArgumentOutOfRangeException.ThrowIfLessThan(JournalMaxAgeDays, MinimumJournalDays);
    }
}
