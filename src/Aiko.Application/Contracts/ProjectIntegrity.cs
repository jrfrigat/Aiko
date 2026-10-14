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
/// One line of an integrity report: which path, what happened to it, and how much it matters. <c>Path</c> is
/// relative to <c>.aiko</c>, with forward slashes, and a file that appeared since the baseline is
/// <see cref="DiagnosticSeverity.Ok"/>: files are the user's to add, and keeping project data in files is what
/// makes that normal.
/// </summary>
public sealed record IntegrityFinding(
    IntegrityFindingKind Kind,
    DiagnosticSeverity Severity,
    string Path,
    string Summary);

/// <summary>
/// Result of checking one project's <c>.aiko</c>: whether it was checked against a baseline at all, and every
/// path that does not match it. <c>ProjectHandle</c> is what a person types for the project - its slug, or its
/// id when it has none - and <c>Findings</c> is everything that was not intact plus everything the baseline
/// does not know about.
/// </summary>
public sealed record ProjectIntegrityReport(
    string ProjectId,
    string ProjectName,
    string ProjectHandle,
    bool HasBaseline,
    DateTimeOffset? SealedAt,
    int ComparedFiles,
    IReadOnlyList<IntegrityFinding> Findings)
{
    /// <summary>Whether anything needs attention; a file that appeared since the baseline does not.</summary>
    public bool HasProblems =>
        Findings.Any(finding => finding.Severity != DiagnosticSeverity.Ok);
}

/// <summary>One file in the baseline: its path relative to <c>.aiko</c>, with forward slashes, and the
/// lower-case hexadecimal SHA-256 of its bytes.</summary>
public sealed record IntegrityEntry(string Path, string Sha256);

/// <summary>
/// The baseline a project's data is compared against, as <c>.aiko/manifest.json</c> holds it.
/// </summary>
/// <remarks>
/// A checksum of every project file, recorded when someone asks for it rather than rewritten on every save:
/// rewriting it would hide what it exists to show, because a card write would fold a manual edit into the
/// baseline and the next check would call the result intact. Sealing is therefore an explicit act, and the
/// file is one a person can read, diff and delete.
/// </remarks>
public sealed record IntegrityManifestDocument(
    int SchemaVersion,
    long Revision,
    DateTimeOffset SealedAt,
    IReadOnlyList<IntegrityEntry> Entries);

/// <summary>
/// Checks that a project's data under <c>.aiko</c> is intact, and records the baseline it is compared
/// against. The check reads and hashes and repairs nothing - the same split as <c>aiko doctor</c> against
/// <c>aiko repair --fix</c> - and sealing is not a repair either: it says "this is what the data looks like
/// now", which is what a person who has just edited a file by hand and accepts the result wants, and it is
/// why the two are separate calls rather than one that always refreshes.
/// </summary>
public interface IProjectIntegrityCheck
{
    /// <summary>Checks a project's data against its baseline without changing anything on disk.</summary>
    ValueTask<ProjectIntegrityReport> VerifyAsync(string projectId, CancellationToken cancellationToken);

    /// <summary>
    /// Records the project's current state as the baseline, and answers with the check that follows it.
    /// </summary>
    ValueTask<ProjectIntegrityReport> SealAsync(string projectId, CancellationToken cancellationToken);
}
