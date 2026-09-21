namespace Aiko.Application.Installation;

/// <summary>
/// How an install, an update or a report-only run ended.
/// </summary>
/// <remarks>
/// <see cref="UpToDate"/> is its own outcome rather than a quiet kind of success: an update that found
/// nothing to do must be tellable from one that refused to act, and a caller that cannot tell them apart
/// reports a no-op as a failure - or worse, a refusal as success.
/// </remarks>
public enum InstallationOutcome
{
    /// <summary>The installed version is already the resolved one; nothing was written.</summary>
    UpToDate,

    /// <summary>The product was installed where there was nothing installed before.</summary>
    Installed,

    /// <summary>An existing installation was replaced with the resolved version.</summary>
    Updated,

    /// <summary>Nothing was written, and the report says why.</summary>
    Refused
}
