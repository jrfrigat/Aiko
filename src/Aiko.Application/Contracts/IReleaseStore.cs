namespace Aiko.Application.Contracts;

/// <summary>
/// What a caller asks for when it records a release.
/// </summary>
/// <remarks>
/// The moment is not among the fields on purpose: a release is recorded when it is recorded, and a caller that
/// could name the moment could write a history whose order nobody can check. The store stamps the time.
/// </remarks>
/// <param name="Version">
/// The tag the release was published under, for example <c>v0.1.3</c> or <c>v0.1.3-pre</c>.
/// </param>
/// <param name="SchemeId">Identifier of the release scheme the release followed.</param>
/// <param name="Cards">
/// Cards that went into the release, by their identifiers. An empty list is a statement rather than an
/// omission: it says the release carried no card, which is what a release of uncarded fixes does.
/// </param>
/// <param name="Notes">What the person wants remembered about this release, or null.</param>
public sealed record RecordReleaseRequest(
    string Version,
    string SchemeId,
    IReadOnlyList<string>? Cards = null,
    string? Notes = null);

/// <summary>
/// One release, as the project remembers it.
/// </summary>
/// <param name="Version">The tag the release was published under, for example <c>v0.1.3</c>.</param>
/// <param name="SchemeId">Identifier of the scheme the release followed.</param>
/// <param name="ReleasedAt">When the release was recorded.</param>
/// <param name="Notes">What the person wanted remembered about it, or null.</param>
/// <param name="Cards">
/// The cards that went into the release. The list is explicit: a card is in a release because it was named
/// when the release was recorded, not because of the state it happened to be in that day.
/// </param>
public sealed record ReleaseRecord(
    string Version,
    string SchemeId,
    DateTimeOffset ReleasedAt,
    string? Notes,
    IReadOnlyList<string> Cards);

/// <summary>
/// The release history: <c>.aiko/releases.json</c>.
/// </summary>
/// <remarks>
/// Entries are held newest first, which is both how the file is written and how a history is read: whoever
/// opens the document, by eye or by screen, wants the release that shipped yesterday at the top. The order is
/// therefore a property of the stored document rather than a sort done at reading time - sorting by
/// <see cref="ReleaseRecord.ReleasedAt"/> would leave two releases recorded in the same second in an order
/// nobody could predict.
/// </remarks>
/// <param name="SchemaVersion">Schema version of the file.</param>
/// <param name="Entries">The releases, newest first.</param>
public sealed record ReleaseDocument(
    int SchemaVersion,
    IReadOnlyList<ReleaseRecord> Entries)
{
    /// <summary>The most recent release, or null when nothing has been released yet.</summary>
    public ReleaseRecord? Latest => Entries.Count > 0 ? Entries[0] : null;

    /// <summary>
    /// The release published under <paramref name="version"/>, or null when no release carries it.
    /// </summary>
    /// <remarks>
    /// Compared without case and without surrounding space, because <c>v0.1.3</c>, <c>V0.1.3</c> and
    /// <c>" v0.1.3 "</c> are one version as far as a reader is concerned, and a lookup that answered "no such
    /// release" to one of them would be a lookup nobody could trust.
    /// </remarks>
    /// <param name="version">Version to look for.</param>
    public ReleaseRecord? Find(string version) =>
        Entries.FirstOrDefault(entry =>
            StringComparer.OrdinalIgnoreCase.Equals(entry.Version.Trim(), version?.Trim()));
}

/// <summary>
/// The record of what this project released.
/// </summary>
/// <remarks>
/// Like the command queue, this is a document beside the project's other documents rather than a projection of
/// the database: losing it would lose the answer to "which cards went into v0.1.3", which nothing else in Aiko
/// knows. It is written atomically under a per-project lock, so two agents recording two releases cannot lose
/// each other's write.
/// </remarks>
public interface IReleaseStore
{
    /// <summary>
    /// Reads the project's release history, newest first.
    /// </summary>
    /// <param name="projectId">Project to read, by id or by its readable handle.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<ReleaseDocument> ReadAsync(string projectId, CancellationToken cancellationToken);

    /// <summary>
    /// Records a release and returns the record as it was stored.
    /// </summary>
    /// <param name="projectId">Project to write to.</param>
    /// <param name="request">What the caller asked to record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<ReleaseRecord> RecordAsync(
        string projectId,
        RecordReleaseRequest request,
        CancellationToken cancellationToken);
}
