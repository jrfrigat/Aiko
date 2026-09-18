using System.Globalization;
using System.Text.Json.Serialization;
using Aiko.Domain.Workflow;

namespace Aiko.Domain.Cards;

/// <summary>
/// A card is the durable source of task or story state on the project board.
/// </summary>
/// <param name="Reference">Project-qualified identifier.</param>
/// <param name="Kind">
/// Card type id, for example <c>Story</c> or <c>Epic</c>. The type is defined by the workflow of the same
/// name (<see cref="WorkflowId"/>), so a project can add its own types without a code change.
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
/// The size step of the project's grid (ТЗ §10), for example <c>M</c>. Null on cards written before the
/// grid existed or never sized; an unsized card is neutral in the formula. The agent assigns it from the
/// grid's descriptions, which is why the grid carries them.
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
    /// Requirements are the free-form answer to "what must this card do?" - the text a person types when
    /// the title alone is not enough. They are prose rather than structure, so they live in the card's own
    /// metadata instead of widening every card document with a field most of them leave empty.
    /// </remarks>
    public const string RequirementsMetadataKey = "requirements";

    /// <summary>
    /// The key a card's last estimate is recorded under in <see cref="Metadata"/>: the moment the size and
    /// the criterion scores were last written.
    /// </summary>
    /// <remarks>
    /// The scores alone cannot say whether they describe the card as it is now. A stage is closed because its
    /// work is done, and the readiness score is the only place that fact shows, so completing a stage with a
    /// score nobody refreshed would record a card that looks less ready than it is. The timestamp is what
    /// makes "was the card re-estimated in this run?" answerable.
    /// </remarks>
    public const string EstimatedAtMetadataKey = "estimatedAt";

    /// <summary>
    /// When the card was last estimated in round-trip form, or null when it never was. Unreadable text reads
    /// as "never estimated", which is the safe answer: a stale score must not look fresh.
    /// </summary>
    [JsonIgnore]
    public DateTimeOffset? EstimatedAt =>
        Metadata.TryGetValue(EstimatedAtMetadataKey, out var value) &&
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
            ? at
            : null;

    /// <summary>
    /// The key a card's original request is stored under in <see cref="Metadata"/>: what was asked for, in the
    /// words of whoever asked, before anyone turned it into a task.
    /// </summary>
    /// <remarks>
    /// The request is fixed once the card leaves the backlog, because "what was asked" stops being true the
    /// moment it is edited; what changes as the work is understood is the requirements. Like the requirements it
    /// is prose, so it lives in the card's own metadata instead of widening every card document.
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
    /// "nothing was asked" are different facts, and only the first one is true of an old card.
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
    /// Only the backlog may still be corrected - a typo, a clarification - because nothing has been worked on
    /// yet. Once the card is in its pipeline the request is a record of what was asked, and the way to say that
    /// the work changed is the requirements. The rule lives here, next to the metadata it protects, so the HTTP
    /// path and the MCP path refuse with one voice.
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

    /// <summary>
    /// The note that explains a requirements change, or null when the text did not actually change.
    /// </summary>
    /// <remarks>
    /// The feed is where a reader asks "why is this different now?", so a change is explained where it happened
    /// rather than left to be noticed. Clearing the text counts as a change: the card no longer asks what it
    /// asked a moment ago.
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
