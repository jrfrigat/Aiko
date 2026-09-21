using Aiko.Application.Contracts;

namespace Aiko.Server.Contracts;

/// <summary>
/// One line of the release history: what a person reads before opening anything.
/// </summary>
/// <remarks>
/// The count of cards rather than the cards themselves: a screen drawing a history draws a number, and a
/// history that carried every list would pay for them on every opening. The version arrives as it was
/// recorded - the tag - and whether the release was a preliminary one is read from that tag's suffix by
/// whoever draws it, because the suffix is a fact about the tag rather than a second one about the record.
/// </remarks>
/// <param name="Version">The tag the release was published under.</param>
/// <param name="SchemeId">Identifier of the scheme the release followed.</param>
/// <param name="ReleasedAt">When the release was recorded.</param>
/// <param name="Cards">How many cards the release named.</param>
public sealed record ReleaseHistoryItem(
    string Version,
    string SchemeId,
    DateTimeOffset ReleasedAt,
    int Cards);

/// <summary>
/// One release with the cards that went into it.
/// </summary>
/// <param name="Version">The tag the release was published under.</param>
/// <param name="SchemeId">Identifier of the scheme the release followed.</param>
/// <param name="ReleasedAt">When the release was recorded.</param>
/// <param name="Notes">What was remembered about this release, or null.</param>
/// <param name="Cards">The cards the release named; an empty list says it carried none.</param>
public sealed record ReleaseView(
    string Version,
    string SchemeId,
    DateTimeOffset ReleasedAt,
    string? Notes,
    IReadOnlyList<string> Cards)
{
    /// <summary>The view of a stored record.</summary>
    /// <param name="record">Record to show.</param>
    public static ReleaseView From(ReleaseRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new ReleaseView(
            record.Version,
            record.SchemeId,
            record.ReleasedAt,
            record.Notes,
            record.Cards);
    }
}
