namespace Aiko.Application.Contracts;

/// <summary>
/// What one integrity finding is about.
/// </summary>
public enum IntegrityFindingKind
{
    /// <summary>A project document that does not parse, so nothing can read it.</summary>
    Document,

    /// <summary>A file the baseline lists that is no longer on disk.</summary>
    Missing,

    /// <summary>A file whose bytes differ from the baseline.</summary>
    Changed,

    /// <summary>A file the baseline does not list.</summary>
    Added
}

/// <summary>
/// One line of an integrity report: which path, what happened to it, and how much it matters.
/// </summary>
/// <param name="Kind">What the finding is about.</param>
/// <param name="Severity">
/// How much attention it needs. A file that appeared since the baseline is <see cref="DiagnosticSeverity.Ok"/>:
/// files are the user's to add, and the point of keeping project data in files is that they may.
/// </param>
/// <param name="Path">Path of the file, relative to <c>.aiko</c>, with forward slashes.</param>
/// <param name="Summary">Human-readable line, including what to do about it when there is something.</param>
public sealed record IntegrityFinding(
    IntegrityFindingKind Kind,
    DiagnosticSeverity Severity,
    string Path,
    string Summary);

/// <summary>
/// Result of checking one project's <c>.aiko</c>: whether it was checked against a baseline at all, and every
/// path that does not match it.
/// </summary>
/// <param name="ProjectId">Id of the project that was checked.</param>
/// <param name="ProjectName">Its display name, so a report over several projects says which is which.</param>
/// <param name="ProjectHandle">The handle a person types for it - its slug, or its id when it has none.</param>
/// <param name="HasBaseline">Whether a sealed baseline existed to compare against.</param>
/// <param name="SealedAt">When that baseline was recorded, or null when there is none.</param>
/// <param name="ComparedFiles">How many project files the check read and hashed.</param>
/// <param name="Findings">Everything that was not intact, and everything the baseline does not know about.</param>
public sealed record ProjectIntegrityReport(
    string ProjectId,
    string ProjectName,
    string ProjectHandle,
    bool HasBaseline,
    DateTimeOffset? SealedAt,
    int ComparedFiles,
    IReadOnlyList<IntegrityFinding> Findings)
{
    /// <summary>
    /// Whether anything needs attention. A file that appeared since the baseline does not: a project whose data
    /// lives in files gains them whenever its owner likes.
    /// </summary>
    public bool HasProblems =>
        Findings.Any(finding => finding.Severity != DiagnosticSeverity.Ok);
}

/// <summary>
/// One file in the baseline: its path relative to <c>.aiko</c> and the SHA-256 of its bytes.
/// </summary>
/// <param name="Path">Path relative to <c>.aiko</c>, with forward slashes.</param>
/// <param name="Sha256">Lower-case hexadecimal SHA-256 of the file's bytes.</param>
public sealed record IntegrityEntry(string Path, string Sha256);

/// <summary>
/// The baseline a project's data is compared against, as <c>.aiko/manifest.json</c> holds it.
/// </summary>
/// <remarks>
/// A checksum of every project file, recorded when someone asks for it rather than rewritten on every save.
/// Rewriting it on every save would hide exactly what it exists to show: a card write would fold a manual edit
/// into the baseline, and the next check would call the result intact. Sealing is therefore an explicit act,
/// and the file is one a person can read, diff and delete.
/// </remarks>
/// <param name="SchemaVersion">Document shape version.</param>
/// <param name="Revision">Optimistic revision, bumped on every seal.</param>
/// <param name="SealedAt">When the baseline was recorded.</param>
/// <param name="Entries">The files the baseline covers, ordered by path.</param>
public sealed record IntegrityManifestDocument(
    int SchemaVersion,
    long Revision,
    DateTimeOffset SealedAt,
    IReadOnlyList<IntegrityEntry> Entries);

/// <summary>
/// Checks that a project's data under <c>.aiko</c> is intact, and records the baseline it is compared against.
/// </summary>
/// <remarks>
/// The check reads and hashes; it repairs nothing, and a repair is a separate decision - the same split as
/// <c>aiko doctor</c> against <c>aiko repair --fix</c>. Sealing is not a repair either: it says "this is what
/// the data looks like now", which is what a person who has just edited a file by hand and accepts the result
/// wants, and it is why the two are separate calls rather than one that always refreshes.
/// </remarks>
public interface IProjectIntegrityCheck
{
    /// <summary>
    /// Checks a project's data against its baseline without changing anything on disk.
    /// </summary>
    /// <param name="projectId">Project id or readable handle.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<ProjectIntegrityReport> VerifyAsync(string projectId, CancellationToken cancellationToken);

    /// <summary>
    /// Records the project's current state as the baseline, and answers with the check that follows it.
    /// </summary>
    /// <param name="projectId">Project id or readable handle.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<ProjectIntegrityReport> SealAsync(string projectId, CancellationToken cancellationToken);
}
