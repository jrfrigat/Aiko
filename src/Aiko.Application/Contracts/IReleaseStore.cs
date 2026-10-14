namespace Aiko.Application.Contracts;

/// <summary>
/// What a caller asks for when it records a release. The moment is not a field: the store stamps the time,
/// and a caller that could name it could write a history whose order nobody can check. An empty <c>Cards</c>
/// list is a statement rather than an omission - the release carried no card.
/// </summary>
public sealed record RecordReleaseRequest(
    string Version,
    string SchemeId,
    IReadOnlyList<string>? Cards = null,
    string? Notes = null);

/// <summary>
/// One release, as the project remembers it: <c>Cards</c> is the list named when the release was recorded,
/// not the state the cards happened to be in that day.
/// </summary>
public sealed record ReleaseRecord(
    string Version,
    string SchemeId,
    DateTimeOffset ReleasedAt,
    string? Notes,
    IReadOnlyList<string> Cards);

/// <summary>The release history: <c>.aiko/releases.json</c>, entries newest first.</summary>
/// <remarks>
/// The order is a property of the stored document, not a sort done at reading time: sorting by
/// <see cref="ReleaseRecord.ReleasedAt"/> would leave two releases recorded in the same second in an order
/// nobody could predict.
/// </remarks>
public sealed record ReleaseDocument(
    int SchemaVersion,
    IReadOnlyList<ReleaseRecord> Entries)
{
    /// <summary>The most recent release, or null when nothing has been released yet.</summary>
    public ReleaseRecord? Latest => Entries.Count > 0 ? Entries[0] : null;

    /// <summary>
    /// The release published under <paramref name="version"/>, or null when no release carries it. Compared
    /// without case and without surrounding space, so <c>v0.1.3</c>, <c>V0.1.3</c> and <c>" v0.1.3 "</c> are
    /// one version.
    /// </summary>
    public ReleaseRecord? Find(string version) =>
        Entries.FirstOrDefault(entry =>
            StringComparer.OrdinalIgnoreCase.Equals(entry.Version.Trim(), version?.Trim()));
}

/// <summary>
/// The record of what this project released: a document beside the project's other documents rather than a
/// projection of the database, because losing it would lose which cards went into <c>v0.1.3</c>. Written
/// atomically under a per-project lock, so two agents recording two releases cannot lose each other's write.
/// Projects are named by id or by their readable handle.
/// </summary>
public interface IReleaseStore
{
    /// <summary>Reads the project's release history, newest first.</summary>
    ValueTask<ReleaseDocument> ReadAsync(string projectId, CancellationToken cancellationToken);

    /// <summary>Records a release and returns the record as it was stored.</summary>
    ValueTask<ReleaseRecord> RecordAsync(
        string projectId,
        RecordReleaseRequest request,
        CancellationToken cancellationToken);
}
