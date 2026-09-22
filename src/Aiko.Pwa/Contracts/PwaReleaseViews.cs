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
internal sealed record ReleaseView(
    string Version,
    string SchemeId,
    DateTimeOffset ReleasedAt,
    string? Notes,
    IReadOnlyList<string> Cards);
