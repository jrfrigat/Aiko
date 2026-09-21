namespace Aiko.Application.Installation;

/// <summary>
/// What an installation run did, and what it found: the record a caller prints and a person acts on.
/// </summary>
/// <remarks>
/// Both versions travel in the report because <c>--check</c> answers "installed X, available Y" - a report
/// that carried only the outcome would force every caller to resolve the versions again for itself.
/// </remarks>
/// <param name="Outcome">How the run ended.</param>
/// <param name="Installed">What was installed before the run, or null when nothing was.</param>
/// <param name="Available">The resolved release, or null when it could not be resolved.</param>
/// <param name="Steps">The steps the run went through, in order.</param>
/// <param name="Summary">One line a caller can print without interpreting the rest.</param>
public sealed record InstallationReport(
    InstallationOutcome Outcome,
    InstalledVersion? Installed,
    InstalledVersion? Available,
    IReadOnlyList<InstallationStep> Steps,
    string Summary);
