using Aiko.Domain.Cards;

namespace Aiko.Application.Contracts;

/// <summary>
/// One note in a card's discussion: who wrote it, what it says and when. <c>Origin</c> is set only when the
/// note was written into another project's card - it then carries the reporting project, the card it answers
/// and the adapter, so the neighbour's feed shows where the reply came from - and a note written where it
/// lives has none.
/// </summary>
public sealed record CardComment(
    string Id,
    string Author,
    string Body,
    DateTimeOffset CreatedAtUtc,
    CardOrigin? Origin = null);

/// <summary>
/// The notes left on a card: <c>discussion.json</c> inside the card's own folder, next to <c>card.json</c>.
/// </summary>
/// <remarks>
/// A note is authored content, so it lives with the card and Aiko keeps no second copy: an entry the user
/// wrote is not analytics, and the daemon must not be the only place it exists.
/// </remarks>
public interface ICardDiscussionStore
{
    /// <summary>Reads a card's notes, oldest first.</summary>
    ValueTask<IReadOnlyList<CardComment>> ListAsync(CardReference card, CancellationToken cancellationToken);

    /// <summary>
    /// Appends one note to a card's discussion and returns it, with the provenance of a cross-project write
    /// when there is one; an empty note is rejected.
    /// </summary>
    ValueTask<CardComment> AppendAsync(
        CardReference card,
        string author,
        string body,
        CardOrigin? origin,
        CancellationToken cancellationToken);
}

/// <summary>The <c>discussion.json</c> document: the notes of one card, oldest first.</summary>
public sealed record DiscussionDocument(int SchemaVersion, IReadOnlyList<CardComment> Entries);

/// <summary>A note as a client sends it; a null author is the local user.</summary>
public sealed record AddCommentRequest(string? Author, string Body);
