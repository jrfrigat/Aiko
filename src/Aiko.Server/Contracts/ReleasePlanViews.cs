using Aiko.Application.Releases;

namespace Aiko.Server.Contracts;

/// <summary>
/// One card of a release plan, as the release screen reads it.
/// </summary>
/// <param name="CardId">Card the version waits for.</param>
/// <param name="Title">Its current title, or null when the project no longer has the card.</param>
/// <param name="State">Where the card got to.</param>
/// <param name="BlockedBy">The cards that hold it, by id; empty unless the state is blocked.</param>
public sealed record ReleasePlanCardView(
    string CardId,
    string? Title,
    ReleasePlanCardState State,
    IReadOnlyList<string> BlockedBy);

/// <summary>
/// One version's plan with how far it is from being releasable.
/// </summary>
/// <remarks>
/// The counts travel beside the cards rather than being counted by the screen: they come from the one
/// readiness read the daemon already does, and a second count would be a second answer to the same question.
/// </remarks>
/// <param name="Version">The tag the version will be published under.</param>
/// <param name="SchemeId">Identifier of the scheme the version will follow.</param>
/// <param name="IsCurrent">Whether new cards flow into this plan.</param>
/// <param name="ReleasedAt">When the version was recorded, or null while it is still planned.</param>
/// <param name="Notes">What is remembered about this version, or null.</param>
/// <param name="Cards">The composition, in the order the cards joined.</param>
/// <param name="Finished">How many of them reached the end of their pipeline.</param>
/// <param name="Blocked">How many wait for another card.</param>
/// <param name="InWork">How many still have work in them.</param>
/// <param name="Missing">How many cards the project no longer has.</param>
/// <param name="IsReleasable">Whether every planned card has finished.</param>
public sealed record ReleasePlanView(
    string Version,
    string SchemeId,
    bool IsCurrent,
    DateTimeOffset? ReleasedAt,
    string? Notes,
    IReadOnlyList<ReleasePlanCardView> Cards,
    int Finished,
    int Blocked,
    int InWork,
    int Missing,
    bool IsReleasable)
{
    /// <summary>The view of one plan as the daemon read it.</summary>
    /// <param name="report">The plan and the readiness of its cards.</param>
    public static ReleasePlanView From(ReleasePlanReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return new ReleasePlanView(
            report.Plan.Version,
            report.Plan.SchemeId,
            report.Plan.IsCurrent,
            report.Plan.ReleasedAt,
            report.Plan.Notes,
            [.. report.Readiness.Cards.Select(card => new ReleasePlanCardView(
                card.CardId,
                card.Title,
                card.State,
                card.BlockedBy))],
            report.Readiness.Finished,
            report.Readiness.Blocked,
            report.Readiness.InWork,
            report.Readiness.Missing,
            report.Readiness.IsReleasable);
    }
}
