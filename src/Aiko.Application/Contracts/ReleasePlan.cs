namespace Aiko.Application.Contracts;

/// <summary>
/// One card of a release plan: what a version is waiting for, and when it was put there.
/// </summary>
/// <remarks>
/// The entry is not the card: it carries the id and what the plan has to say about it, so a plan stays
/// readable when the card it names has been archived, renamed or removed. <see cref="CarriedFromVersion"/>
/// is the one thing a plan says about its own past - a card that outlived a released version arrived here
/// from that version's plan, and saying so is what keeps a carry-over from looking like a fresh plan.
/// </remarks>
/// <param name="CardId">Card the plan waits for.</param>
/// <param name="AddedAt">When the card joined this plan.</param>
/// <param name="Note">What the plan wants remembered about this card, or null.</param>
/// <param name="CarriedFromVersion">
/// The released version whose plan this card came from, or null when it was planned here first.
/// </param>
public sealed record ReleasePlanEntry(
    string CardId,
    DateTimeOffset AddedAt,
    string? Note = null,
    string? CarriedFromVersion = null);

/// <summary>
/// What a version is waiting for: the cards that have to be done for it to ship.
/// </summary>
/// <remarks>
/// The planned counterpart of <see cref="ReleaseRecord"/>, and deliberately not the same thing: the record
/// says what shipped and is written once, the plan says what is expected and changes as work appears. A
/// version has at most one plan, so the version is the key.
/// </remarks>
/// <param name="Version">
/// The tag the version will be published under, for example <c>v0.2.0</c> - the same shape a recorded
/// release carries, because it is the same tag.
/// </param>
/// <param name="SchemeId">Identifier of the release scheme this version will follow.</param>
/// <param name="Cards">The composition, in the order the cards joined.</param>
/// <param name="IsCurrent">
/// Whether new cards flow into this plan (see <see cref="ReleasePlanDocument.Current"/>). At most one
/// unreleased plan of a project carries it.
/// </param>
/// <param name="CreatedAt">When the plan was first written.</param>
/// <param name="ReleasedAt">When the version was recorded, or null while the version is still planned.</param>
/// <param name="Notes">What the person wants remembered about this version, or null.</param>
public sealed record ReleasePlan(
    string Version,
    string SchemeId,
    IReadOnlyList<ReleasePlanEntry> Cards,
    bool IsCurrent,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReleasedAt,
    string? Notes)
{
    /// <summary>True once the version this plan describes has been recorded.</summary>
    public bool IsReleased => ReleasedAt is not null;

    /// <summary>
    /// Whether this plan already names <paramref name="cardId"/>.
    /// </summary>
    /// <remarks>
    /// Compared without case and without surrounding space, exactly as the release history compares a card
    /// id: the same card named twice would otherwise be planned twice.
    /// </remarks>
    /// <param name="cardId">Card to look for.</param>
    public bool Names(string cardId) =>
        Cards.Any(entry => Same(entry.CardId, cardId));

    /// <summary>
    /// Whether two values name the same thing - the same version, or the same card id.
    /// </summary>
    /// <remarks>
    /// Compared without case and without surrounding space, the way the release history compares a version:
    /// <c>v0.2.0</c>, <c>V0.2.0</c> and <c>" v0.2.0 "</c> are one version as far as a reader is concerned, and
    /// a plan that answered "no such version" to one of them would be a plan nobody could trust.
    /// <para>
    /// Public because the store compares with it too: the rule has to be the same one on both sides of the
    /// port, or a plan could be found by one caller and missed by the next.
    /// </para>
    /// </remarks>
    /// <param name="left">One value, or null.</param>
    /// <param name="right">The other value, or null.</param>
    public static bool Same(string? left, string? right) =>
        StringComparer.OrdinalIgnoreCase.Equals(left?.Trim(), right?.Trim());
}

/// <summary>
/// The release plans of a project: <c>.aiko/release-plan.json</c>.
/// </summary>
/// <remarks>
/// Held most recently created first, which is both how the file is written and how the list is read: the
/// plan somebody just made is the one they are looking for. The order is a property of the stored document
/// rather than a sort done at reading time, for the reason the release history states the same rule - a
/// sort over two plans made in the same second has no order to give.
/// </remarks>
/// <param name="SchemaVersion">Schema version of the file.</param>
/// <param name="Plans">The plans, most recently created first.</param>
public sealed record ReleasePlanDocument(
    int SchemaVersion,
    IReadOnlyList<ReleasePlan> Plans)
{
    /// <summary>The most recently created plan, or null when nothing is planned.</summary>
    public ReleasePlan? Latest => Plans.Count > 0 ? Plans[0] : null;

    /// <summary>
    /// The plan new cards flow into, or null when the project plans nothing.
    /// </summary>
    /// <remarks>
    /// The plan that says so wins, and a plan whose version has been recorded cannot be it: work that
    /// appears after a version shipped belongs to the next one, not to a version nobody can change.
    /// </remarks>
    public ReleasePlan? Current =>
        Plans.FirstOrDefault(plan => plan.IsCurrent && !plan.IsReleased);

    /// <summary>
    /// The plan of <paramref name="version"/>, or null when the version is not planned.
    /// </summary>
    /// <param name="version">Version to look for.</param>
    public ReleasePlan? Find(string version) =>
        Plans.FirstOrDefault(plan => ReleasePlan.Same(plan.Version, version));

    /// <summary>
    /// The plan that takes work outliving <paramref name="version"/>: the current plan when it is another
    /// version, otherwise the most recently created unreleased plan that is not that version, or null when
    /// the project has nothing else planned.
    /// </summary>
    /// <param name="version">Version whose plan is being left behind.</param>
    public ReleasePlan? Next(string? version)
    {
        if (Current is { } current && !ReleasePlan.Same(current.Version, version))
        {
            return current;
        }

        return Plans.FirstOrDefault(plan => !plan.IsReleased && !ReleasePlan.Same(plan.Version, version));
    }
}

/// <summary>
/// What a caller asks a plan to become.
/// </summary>
/// <remarks>
/// An intent rather than a document to overwrite: the store applies it to the plan as it stands, under the
/// project's lock, so two callers adding two cards cannot lose each other's write. A field left null is
/// left as it is; <see cref="Notes"/> passed as an empty string clears the note.
/// </remarks>
/// <param name="Version">Version whose plan is being changed; the plan is created when it does not exist.</param>
/// <param name="SchemeId">Scheme the version follows; required when the plan is being created.</param>
/// <param name="AddCards">Cards to add; ones already planned are left alone.</param>
/// <param name="RemoveCards">Cards to take out.</param>
/// <param name="IsCurrent">True to make this the plan new cards flow into, false to stop it being one.</param>
/// <param name="Notes">The note to store, or null to leave the stored one alone.</param>
public sealed record ReleasePlanUpdate(
    string Version,
    string? SchemeId = null,
    IReadOnlyList<string>? AddCards = null,
    IReadOnlyList<string>? RemoveCards = null,
    bool? IsCurrent = null,
    string? Notes = null);

/// <summary>
/// The rules a release plan obeys: what it has to carry, and when it may not be changed at all.
/// </summary>
/// <remarks>
/// The same reasoning as <c>ReleaseRecords</c> and <c>CardCommands</c>: these are rules about plans rather
/// than about one caller, so the tool, the endpoint and a spec read them from one place instead of each
/// remembering them.
/// </remarks>
public static class ReleasePlans
{
    /// <summary>
    /// Rejects a plan that names nothing a reader could find again.
    /// </summary>
    /// <param name="version">Version the plan is for.</param>
    /// <param name="schemeId">Scheme the plan follows.</param>
    /// <exception cref="ArgumentException">The version or the scheme is missing or unusable.</exception>
    public static void Validate(string? version, string? schemeId) =>
        // A plan is for a version that will be tagged, so it answers to the rule a release answers to, and
        // that rule is stated once, in the release vocabulary.
        ReleaseRecords.Validate(version, schemeId);

    /// <summary>
    /// The card list as a plan keeps it: trimmed, and without the blanks that name no card.
    /// </summary>
    /// <param name="cards">Cards the caller named, or null.</param>
    public static IReadOnlyList<string> NormalizeCards(IReadOnlyList<string>? cards) =>
        ReleaseRecords.NormalizeCards(cards);

    /// <summary>
    /// Why the plan may not be changed, or null when it may be.
    /// </summary>
    /// <remarks>
    /// A plan whose version was recorded is closed: it is the answer to "what was this version waiting
    /// for", and a change made afterwards would make that answer untrue. There is no reopening - the next
    /// version gets its own plan.
    /// </remarks>
    /// <param name="plan">The plan as it stands, or null when it does not exist yet.</param>
    public static string? RefuseUpdate(ReleasePlan? plan) =>
        plan is { ReleasedAt: { } releasedAt }
            ? $"release plan '{plan.Version}' is closed: the version was recorded at {releasedAt:u}. A closed "
              + "plan says what that version was waiting for, so plan this work in another version instead."
            : null;
}
