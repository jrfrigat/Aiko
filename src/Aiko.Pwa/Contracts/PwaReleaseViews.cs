using Aiko.Application.Releases;

namespace Aiko.Pwa.Contracts;

/// <summary>
/// One line of the project's release history, as the daemon answers it.
/// </summary>
/// <remarks>
/// The client states the shape it reads rather than reusing the daemon's own view type: this project
/// references <c>Aiko.Application</c> and not <c>Aiko.Server</c>, so the server's records are not reachable
/// from here. The mirror is held in place by the JSON contract spec, which requires every type the client
/// reads to be listed in <see cref="Aiko.Pwa.Services.PwaJsonContext"/> - the published client is trimmed,
/// and reflection-based serialization does not work there.
/// </remarks>
/// <param name="Version">The tag the release was published under.</param>
/// <param name="SchemeId">Identifier of the scheme the release followed.</param>
/// <param name="ReleasedAt">When the release was recorded.</param>
/// <param name="Cards">How many cards the release named.</param>
internal sealed record ReleaseHistoryItem(
    string Version,
    string SchemeId,
    DateTimeOffset ReleasedAt,
    int Cards);

/// <summary>
/// One release with the cards that went into it, as the daemon answers it.
/// </summary>
/// <param name="Version">The tag the release was published under.</param>
/// <param name="SchemeId">Identifier of the scheme the release followed.</param>
/// <param name="ReleasedAt">When the release was recorded.</param>
/// <param name="Notes">What was remembered about this release, or null.</param>
/// <param name="Cards">The cards the release named; an empty list says it carried none.</param>
/// <param name="CardTitles">
/// The current title of each named card the daemon could still find; a card missing here is shown by its id.
/// </param>
internal sealed record ReleaseView(
    string Version,
    string SchemeId,
    DateTimeOffset ReleasedAt,
    string? Notes,
    IReadOnlyList<string> Cards,
    IReadOnlyDictionary<string, string>? CardTitles = null);

/// <summary>
/// One card of a release plan, as the daemon answers it.
/// </summary>
/// <param name="CardId">Card the version waits for.</param>
/// <param name="Title">Its current title, or null when the project no longer has the card.</param>
/// <param name="State">
/// Where the card got to. The state travels as the daemon's own enumeration - <c>Aiko.Application</c> is
/// referenced from here, so the vocabulary of states is shared rather than re-spelled on this side.
/// </param>
/// <param name="BlockedBy">The cards that hold it, by id.</param>
internal sealed record ReleasePlanCardView(
    string CardId,
    string? Title,
    ReleasePlanCardState State,
    IReadOnlyList<string> BlockedBy);

/// <summary>
/// One version's plan, as the daemon answers it.
/// </summary>
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
internal sealed record ReleasePlanView(
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
    bool IsReleasable);
