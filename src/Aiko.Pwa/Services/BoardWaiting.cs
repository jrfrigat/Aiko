using Aiko.Application.Contracts;
using Aiko.Domain.Execution;

namespace Aiko.Pwa.Services;

/// <summary>
/// Whether a card is waiting for an answer from the person, which is what the board's answer filter selects.
/// </summary>
/// <remarks>
/// Two facts decide it and neither is inferred from the other: the card's latest run must be waiting for a
/// user, and the last word in its feed must be an agent's. An agent asking a question is what puts a card into
/// the person's hands; the person's own answer takes it back out.
/// </remarks>
public static class BoardWaiting
{
    /// <summary>Whether a run's state is "waiting for a user decision".</summary>
    /// <param name="runState">A run's state, as the board reports it.</param>
    public static bool IsWaitingForUser(string? runState) =>
        StringComparer.OrdinalIgnoreCase.Equals(runState, nameof(StageExecutionState.WaitingForUser));

    /// <summary>
    /// Whether the card is waiting for an answer: its latest run waits for a user, and the last note in its
    /// feed is an agent's.
    /// </summary>
    /// <param name="latestRunState">State of the card's latest run.</param>
    /// <param name="feed">The card's feed, in any order: the latest note by time is the one that counts.</param>
    public static bool AwaitsAnswer(string? latestRunState, IReadOnlyList<CardComment>? feed) =>
        IsWaitingForUser(latestRunState) && !string.IsNullOrWhiteSpace(LastAuthor(feed));

    /// <summary>
    /// The author of the last note in a feed, or null when the feed is empty, or holds nothing but notes of
    /// its own.
    /// </summary>
    /// <remarks>
    /// A note with no author is the local user's: the contract says <c>null</c> is "the local user", and the
    /// client posts exactly that when a person answers. So an author is what an agent's question carries and a
    /// person's answer does not - which is the whole difference this filter is about.
    /// </remarks>
    /// <param name="feed">The card's feed.</param>
    public static string? LastAuthor(IReadOnlyList<CardComment>? feed)
    {
        if (feed is null || feed.Count == 0)
        {
            return null;
        }

        // Ordered by time rather than taken from the end: a store may hand the notes over in any order, and
        // the sort is stable, so notes written in the same instant keep the order they were filed in.
        return feed
            .OrderBy(comment => comment.CreatedAtUtc)
            .Last()
            .Author;
    }
}
