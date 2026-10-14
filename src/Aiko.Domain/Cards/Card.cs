using System.Globalization;
using System.Text.Json.Serialization;
using Aiko.Domain.Workflow;

namespace Aiko.Domain.Cards;

/// <summary>
/// A card is the durable source of task or story state on the project board.
/// </summary>
/// <param name="Reference">Project-qualified identifier.</param>
/// <param name="Kind">
/// Card type id, for example <c>Story</c>. The type is defined by the workflow of the same name
/// (<see cref="WorkflowId"/>), so a project adds its own types without a code change.
/// </param>
/// <param name="Title">Human-readable title.</param>
/// <param name="WorkflowId">Workflow the card moves through.</param>
/// <param name="StageId">Current stage.</param>
/// <param name="Revision">Optimistic concurrency revision.</param>
/// <param name="OwnPriority">The card's own priority, before parents and before the size coefficient.</param>
/// <param name="DeclaredScopeFiles">File patterns the card intends to touch.</param>
/// <param name="ActualChangedFiles">Files the agents actually changed.</param>
/// <param name="Metadata">Free-form card metadata.</param>
/// <param name="Origin">Where a cross-project card came from.</param>
/// <param name="CriterionValues">Per-criterion values keyed by criterion id, when the project scores by criteria.</param>
/// <param name="Size">
/// The size step of the project's grid, for example <c>M</c>. Null on cards written before the grid existed or
/// never sized - an unsized card is neutral in the formula - and the agent assigns it from the grid's own
/// descriptions.
/// </param>
public sealed record Card(
    CardReference Reference,
    string Kind,
    string Title,
    string WorkflowId,
    string StageId,
    long Revision,
    decimal OwnPriority,
    IReadOnlyList<string> DeclaredScopeFiles,
    IReadOnlyList<string> ActualChangedFiles,
    IReadOnlyDictionary<string, string> Metadata,
    CardOrigin? Origin = null,
    IReadOnlyDictionary<string, decimal>? CriterionValues = null,
    string? Size = null)
{
    /// <summary>
    /// The key a card's requirements are stored under in <see cref="Metadata"/>.
    /// </summary>
    /// <remarks>
    /// The free-form answer to "what must this card do?" - prose rather than structure, so it lives in the
    /// card's own metadata instead of widening every card document with a field most of them leave empty.
    /// </remarks>
    public const string RequirementsMetadataKey = "requirements";

    /// <summary>
    /// The key a card's last estimate is recorded under in <see cref="Metadata"/>: the moment the size and
    /// the criterion scores were last written.
    /// </summary>
    /// <remarks>
    /// The scores alone cannot say whether they describe the card as it is now, and a stage closed with a score
    /// nobody refreshed would record a card less ready than it is. The timestamp makes "was the card
    /// re-estimated in this run?" answerable.
    /// </remarks>
    public const string EstimatedAtMetadataKey = "estimatedAt";

    /// <summary>
    /// When the card was last estimated in round-trip form, or null when it never was. Unreadable text reads as
    /// "never estimated": a stale score must not look fresh.
    /// </summary>
    [JsonIgnore]
    public DateTimeOffset? EstimatedAt =>
        Metadata.TryGetValue(EstimatedAtMetadataKey, out var value) &&
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
            ? at
            : null;

    /// <summary>
    /// The key a card's archive mark is stored under in <see cref="Metadata"/>: when the card was taken off the
    /// board.
    /// </summary>
    /// <remarks>
    /// Being out of the way is a reading of the card rather than a place it is moved to: the files, the feed,
    /// the runs and the relations stay as they were, and this mark is the only thing that changes. A stage
    /// would put an archived card back into the pipeline; a folder would have to be kept in step with every
    /// path Aiko derives from a card's directory.
    /// </remarks>
    public const string ArchivedAtMetadataKey = "archivedAt";

    /// <summary>
    /// When the card was archived in round-trip form, or null when it is not in the archive or its mark cannot
    /// be read.
    /// </summary>
    [JsonIgnore]
    public DateTimeOffset? ArchivedAt =>
        Metadata.TryGetValue(ArchivedAtMetadataKey, out var value) &&
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
            ? at
            : null;

    /// <summary>
    /// Whether the card is in the archive: judged by the mark being present, not by its text being readable.
    /// </summary>
    /// <remarks>
    /// A mark nobody can parse still means the card was put away, and reading it as "on the board" would hand a
    /// card back to the queue on the strength of a typo.
    /// </remarks>
    [JsonIgnore]
    public bool IsArchived => Metadata.ContainsKey(ArchivedAtMetadataKey);

    /// <summary>
    /// The metadata with the archive mark set to <paramref name="at"/>, or with it removed when it is null -
    /// which is what returning a card from the archive does.
    /// </summary>
    /// <param name="metadata">Metadata to copy.</param>
    /// <param name="at">Moment the card was archived, or null to take it off the archive.</param>
    public static IReadOnlyDictionary<string, string> WithArchivedAt(
        IReadOnlyDictionary<string, string> metadata,
        DateTimeOffset? at) =>
        WithText(
            metadata,
            ArchivedAtMetadataKey,
            at?.ToString("O", CultureInfo.InvariantCulture));

    /// <summary>
    /// The key a card's original request is stored under in <see cref="Metadata"/>: what was asked for, in the
    /// words of whoever asked, before anyone turned it into a task.
    /// </summary>
    /// <remarks>
    /// Fixed once the card leaves the backlog, because "what was asked" stops being true the moment it is
    /// edited; what changes as the work is understood is the requirements.
    /// </remarks>
    public const string RequestMetadataKey = "request";

    /// <summary>
    /// What the card is asked to do, in the words of whoever wrote it, or null when nobody wrote any.
    /// </summary>
    [JsonIgnore]
    public string? Requirements => Text(RequirementsMetadataKey);

    /// <summary>
    /// What was asked for when the card was written down, or null when it was never recorded.
    /// </summary>
    /// <remarks>
    /// A card written before this key existed has no request, and none is invented for it: "not recorded" and
    /// "nothing was asked" are different facts.
    /// </remarks>
    [JsonIgnore]
    public string? Request => Text(RequestMetadataKey);

    /// <summary>
    /// The metadata with one text replaced: trimmed and stored, and a blank value removes the key rather than
    /// storing an empty one, so "nothing is written" has exactly one representation.
    /// </summary>
    /// <remarks>
    /// A static over the dictionary rather than a method on the card, so several texts can be folded in one
    /// after another without each step reading the card's own - now stale - metadata.
    /// </remarks>
    /// <param name="metadata">Metadata to copy.</param>
    /// <param name="key">Metadata key, for example <see cref="RequestMetadataKey"/>.</param>
    /// <param name="value">The text, or null/blank to remove it.</param>
    public static IReadOnlyDictionary<string, string> WithText(
        IReadOnlyDictionary<string, string> metadata,
        string key,
        string? value)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var updated = new Dictionary<string, string>(metadata, StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(value))
        {
            updated.Remove(key);
        }
        else
        {
            updated[key] = value.Trim();
        }

        return updated;
    }

    /// <summary>
    /// Why the original request may not be changed, or null when it may.
    /// </summary>
    /// <remarks>
    /// Only the backlog may still be corrected, because nothing has been worked on yet; after that the request
    /// is a record of what was asked, and the way to say the work changed is the requirements. Stated next to
    /// the metadata it protects, so the HTTP path and the MCP path refuse with one voice.
    /// </remarks>
    /// <param name="cardId">Card being changed, for the message.</param>
    /// <param name="stageId">Stage the card is in now.</param>
    public static string? RefuseRequestChange(string cardId, string stageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cardId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stageId);
        return StringComparer.Ordinal.Equals(stageId, WorkflowDefinition.BacklogStageId)
            ? null
            : $"the original request of card '{cardId}' is fixed: the card has left the "
                + $"'{WorkflowDefinition.BacklogStageId}' stage and is in '{stageId}'. The request records what "
                + "was asked for when the card was written down; say what changed in the requirements instead.";
    }

    /// <summary>The note that explains a requirements change, or null when the text did not change.</summary>
    /// <remarks>
    /// The feed is where a reader asks "why is this different now?", so a change is explained where it happened.
    /// Clearing the text counts as a change: the card no longer asks what it asked a moment ago.
    /// </remarks>
    /// <param name="before">The stored requirements, or null when the card had none.</param>
    /// <param name="after">The requirements as they will be stored, or null when they were cleared.</param>
    /// <param name="reason">What the caller says the change came from, or null when it said nothing.</param>
    public static string? RequirementsChangeNote(string? before, string? after, string? reason)
    {
        if (string.Equals(before, after, StringComparison.Ordinal))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(reason)
            ? "The task description changed."
            : $"The task description changed: the user asked \"{reason.Trim()}\".";
    }

    private string? Text(string key) =>
        Metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
}
