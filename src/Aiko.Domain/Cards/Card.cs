using System.Globalization;
using System.Text.Json.Serialization;

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
    /// What the card is asked to do, in the words of whoever wrote it, or null when nobody wrote any.
    /// </summary>
    [JsonIgnore]
    public string? Requirements =>
        Metadata.TryGetValue(RequirementsMetadataKey, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
}
