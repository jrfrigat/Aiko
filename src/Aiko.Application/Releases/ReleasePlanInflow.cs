using Aiko.Application.Contracts;
using Aiko.Domain.Cards;

namespace Aiko.Application.Releases;

/// <summary>
/// What became of a newly created card's arrival in a release plan.
/// </summary>
/// <remarks>
/// Exactly one of the three is normally set: the version the card joined, the reason the caller kept it out, or
/// the note saying why no plan took it when that was not the caller's decision. The card's own metadata then
/// tells a reader what happened without anybody having to reconstruct it.
/// </remarks>
/// <param name="PlannedForVersion">The version whose plan took the card, or null.</param>
/// <param name="ExclusionReason">Why the caller kept the card out of every plan, or null.</param>
/// <param name="Note">Why no plan took the card, or null.</param>
public sealed record ReleasePlanAttribution(
    string? PlannedForVersion = null,
    string? ExclusionReason = null,
    string? Note = null);

/// <summary>
/// The inflow of new cards into the version being worked on.
/// </summary>
/// <remarks>
/// Both doors that create a card call this, so the screen's form and the agent cannot disagree about where a
/// new card goes: a card created while a version is planned joins that version's plan, unless the caller says
/// otherwise - and saying otherwise means saying why.
/// <para>
/// It never fails a creation and never invents a version. A project that plans nothing, or a version nobody
/// planned, leaves the card outside every plan and says so in words; a plan that refuses the card - its version
/// was recorded while this card was being made - leaves the card created and records the reason. Neither is
/// silence, because "no plan took it" and "nothing needed planning" would otherwise look the same.
/// </para>
/// </remarks>
public sealed class ReleasePlanInflow(IReleasePlanStore plans)
{
    /// <summary>The value of the release-plan argument that keeps a card out of every plan.</summary>
    public const string None = "none";

    /// <summary>The metadata key naming the version whose plan the card joined.</summary>
    public const string PlannedMetadataKey = "release-plan";

    /// <summary>The metadata key holding the reason a caller kept the card out of the plan.</summary>
    public const string ExclusionMetadataKey = "release-plan-exclusion";

    /// <summary>
    /// The metadata key saying why no plan took the card, when that was not the caller's decision.
    /// </summary>
    public const string NoteMetadataKey = "release-plan-note";

    /// <summary>
    /// Puts the card into the plan it belongs to and reports what happened.
    /// </summary>
    /// <param name="projectId">Project the card is being created in.</param>
    /// <param name="cardId">Id the card will be filed under.</param>
    /// <param name="releasePlan">
    /// The version whose plan should take the card, <c>"none"</c> to keep it out of every plan, or null to let
    /// Aiko choose the version being worked on.
    /// </param>
    /// <param name="exclusionReason">
    /// Why the card is kept out; required when <paramref name="releasePlan"/> is <c>"none"</c>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentException">The caller asked for "none" without saying why.</exception>
    public async ValueTask<ReleasePlanAttribution> AttributeAsync(
        string projectId,
        string cardId,
        string? releasePlan,
        string? exclusionReason,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cardId);
        var asked = releasePlan?.Trim();
        if (!string.IsNullOrEmpty(asked) && StringComparer.OrdinalIgnoreCase.Equals(asked, None))
        {
            if (string.IsNullOrWhiteSpace(exclusionReason))
            {
                throw new ArgumentException(
                    "Leaving a card out of the release is a decision somebody has to be able to read: say why "
                    + "it is out, or leave it in the version being worked on.",
                    nameof(releasePlan));
            }

            return new ReleasePlanAttribution(ExclusionReason: exclusionReason.Trim());
        }

        var document = await plans.ReadAsync(projectId, cancellationToken);
        var target = string.IsNullOrEmpty(asked)
            // The version the project is working on: the plan that says so, or the most recently created one
            // that still has a version ahead of it.
            ? document.Current ?? document.Plans.FirstOrDefault(plan => !plan.IsReleased)
            : document.Find(asked);

        if (target is null)
        {
            return new ReleasePlanAttribution(Note: string.IsNullOrEmpty(asked)
                ? "This project plans no version, so the card joined none. Plan one with "
                  + "aiko_update_release_plan and new cards will flow into it."
                : $"No release plan exists for '{asked}', so the card joined none. Plan that version first with "
                  + "aiko_update_release_plan - a plan is not invented for a version nobody planned.");
        }

        try
        {
            var plan = await plans.UpdateAsync(
                projectId,
                new ReleasePlanUpdate(target.Version, AddCards: [cardId]),
                cancellationToken);
            return new ReleasePlanAttribution(PlannedForVersion: plan.Version);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            // A closed plan refuses the card, and a refusal must not lose the card: it is created, and the
            // answer says why the plan did not take it.
            return new ReleasePlanAttribution(
                Note: $"The plan for '{target.Version}' did not take the card: {exception.Message}");
        }
    }

    /// <summary>
    /// The card's metadata with the outcome written into it.
    /// </summary>
    /// <remarks>
    /// The outcome travels as metadata rather than as a second answer document, because the tool's answer is the
    /// card itself: what a creation did is then readable on the card, by a person and by the next agent, and no
    /// caller's parsing of the answer changes.
    /// </remarks>
    /// <param name="metadata">Metadata the card is being built with.</param>
    /// <param name="attribution">What <see cref="AttributeAsync"/> reported.</param>
    public static IReadOnlyDictionary<string, string> WithAttribution(
        IReadOnlyDictionary<string, string> metadata,
        ReleasePlanAttribution attribution)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(attribution);
        var written = metadata;
        if (!string.IsNullOrWhiteSpace(attribution.PlannedForVersion))
        {
            written = Card.WithText(written, PlannedMetadataKey, attribution.PlannedForVersion);
        }

        if (!string.IsNullOrWhiteSpace(attribution.ExclusionReason))
        {
            written = Card.WithText(written, ExclusionMetadataKey, attribution.ExclusionReason);
        }

        if (!string.IsNullOrWhiteSpace(attribution.Note))
        {
            written = Card.WithText(written, NoteMetadataKey, attribution.Note);
        }

        return written;
    }
}
