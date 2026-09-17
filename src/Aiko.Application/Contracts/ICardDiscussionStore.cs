using Aiko.Domain.Cards;

namespace Aiko.Application.Contracts;

/// <summary>
/// One note in a card's discussion: who wrote it, what it says and when.
/// </summary>
/// <param name="Id">Identifier of the entry.</param>
/// <param name="Author">Who wrote it - a person's name, or the agent the run belongs to.</param>
/// <param name="Body">The note itself, as it was typed.</param>
/// <param name="CreatedAtUtc">When it was written, in UTC.</param>
public sealed record CardComment(string Id, string Author, string Body, DateTimeOffset CreatedAtUtc);

/// <summary>
/// The notes left on a card.
/// </summary>
/// <remarks>
/// A note is authored content, so it lives with the card: <c>discussion.json</c> inside the card's own
/// folder, next to <c>card.json</c>. Aiko keeps no second copy - an entry the user wrote is not analytics,
/// and the daemon must not be the only place it exists.
/// </remarks>
public interface ICardDiscussionStore
{
    /// <summary>
    /// Reads a card's notes, oldest first.
    /// </summary>
    /// <param name="card">Card to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<IReadOnlyList<CardComment>> ListAsync(CardReference card, CancellationToken cancellationToken);

    /// <summary>
    /// Appends one note to a card's discussion and returns it.
    /// </summary>
    /// <param name="card">Card to write to.</param>
    /// <param name="author">Who wrote it.</param>
    /// <param name="body">The note itself; empty notes are rejected.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<CardComment> AppendAsync(
        CardReference card,
        string author,
        string body,
        CancellationToken cancellationToken);
}

/// <summary>
/// The <c>discussion.json</c> document: the notes of one card.
/// </summary>
/// <param name="SchemaVersion">Schema version of the file.</param>
/// <param name="Entries">The notes, oldest first.</param>
public sealed record DiscussionDocument(int SchemaVersion, IReadOnlyList<CardComment> Entries);

/// <summary>
/// A note as a client sends it.
/// </summary>
/// <param name="Author">Who is writing, or null for the local user.</param>
/// <param name="Body">The note.</param>
public sealed record AddCommentRequest(string? Author, string Body);
