namespace Aiko.Infrastructure.Logging;

/// <summary>
/// How much of its own record the daemon keeps: the log file it writes in the background, and the
/// append-only event journal.
/// </summary>
/// <remarks>
/// Settings rather than constants for the same reason the port is: how much history is worth keeping is the
/// installation's decision, and a compiled-in value can only be changed by rebuilding. Trimming is never
/// silent - a dropped log file is named by the file that replaces it, and removed journal rows leave a
/// marker event - so "where did yesterday's log go?" has an answer.
/// </remarks>
/// <param name="MaxFileBytes">Size the daemon log reaches before it is rotated to the next numbered file.</param>
/// <param name="MaxFiles">How many daemon log files are kept in total, the current one included.</param>
/// <param name="JournalMaxAgeDays">Age past which event journal rows are trimmed.</param>
public sealed record LogRetentionSettings(
    int MaxFileBytes = LogRetentionSettings.DefaultMaxFileBytes,
    int MaxFiles = LogRetentionSettings.DefaultMaxFiles,
    int JournalMaxAgeDays = LogRetentionSettings.DefaultJournalMaxAgeDays)
{
    public const int DefaultMaxFileBytes = 8 * 1024 * 1024;

    public const int DefaultMaxFiles = 5;

    public const int DefaultJournalMaxAgeDays = 30;

    /// <summary>Smallest size that still rotates usefully: below it a log turns into a list of its own rotations.</summary>
    public const int MinimumFileBytes = 64 * 1024;

    /// <summary>
    /// Fewest files a rotation may keep: one file is not a rotation but the file overwritten in place, which
    /// is the silent loss this setting exists to prevent.
    /// </summary>
    public const int MinimumFiles = 2;

    /// <summary>Shortest retention: shorter than this and the journal outlives nothing it records.</summary>
    public const int MinimumJournalDays = 1;

    public static LogRetentionSettings Default { get; } = new();

    /// <summary>Throws when a bound would make the daemon lose its own record faster than it can write it.</summary>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxFileBytes, MinimumFileBytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxFiles, MinimumFiles);
        ArgumentOutOfRangeException.ThrowIfLessThan(JournalMaxAgeDays, MinimumJournalDays);
    }
}
