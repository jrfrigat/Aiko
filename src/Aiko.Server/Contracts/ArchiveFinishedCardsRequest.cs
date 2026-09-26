namespace Aiko.Server.Contracts;

/// <summary>
/// Request to put every finished card of a project into the archive at once - the board's "send to archive"
/// action.
/// </summary>
/// <remarks>
/// <para>
/// The body is optional, and that is on purpose: the board asks the question the endpoint's own name asks -
/// "archive the finished ones" - and sending a list of the cards it believes are finished would move the
/// decision to the screen. An empty or absent body therefore means "every card that finished its pipeline".
/// </para>
/// <para>
/// <paramref name="CardIds"/>, when it is given, narrows the same action to the cards named. The release page
/// needs exactly that - the same rule applied to the cards a release carries - and one request shape with a
/// filter is one statement of the rule rather than two that can drift apart.
/// </para>
/// </remarks>
/// <param name="CardIds">Cards to consider, or null or empty for every finished card of the project.</param>
internal sealed record ArchiveFinishedCardsRequest(IReadOnlyList<string>? CardIds = null);
