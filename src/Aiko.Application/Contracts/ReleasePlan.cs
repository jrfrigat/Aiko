namespace Aiko.Application.Contracts;

/// <summary>
/// One card of a release plan: what a version is waiting for, and when it was put there. The entry is not the
/// card - it carries the id and what the plan has to say about it, so a plan stays readable when the card it
/// names has been archived, renamed or removed - and <c>CarriedFromVersion</c> is the one thing a plan says
/// about its own past: a card that outlived a released version arrived here from that version's plan, and
/// saying so is what keeps a carry-over from looking like a fresh plan.
/// </summary>
public sealed record ReleasePlanEntry(
    string CardId,
    DateTimeOffset AddedAt,
    string? Note = null,
    string? CarriedFromVersion = null);

/// <summary>
/// What a version is waiting for: the cards that have to be done for it to ship. The planned counterpart of
/// <see cref="ReleaseRecord"/> and deliberately not the same thing - the record says what shipped and is
/// written once, the plan says what is expected and changes as work appears - so the version is the key, as a
/// version has at most one plan and at most one unreleased plan carries <c>IsCurrent</c>.
/// </summary>
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
    /// Whether this plan already names <paramref name="cardId"/>, compared without case and without
    /// surrounding space: the same card named twice would otherwise be planned twice.
    /// </summary>
    public bool Names(string cardId) =>
        Cards.Any(entry => Same(entry.CardId, cardId));

    /// <summary>
    /// Whether two values name the same thing - the same version, or the same card id - compared without case
    /// and without surrounding space, so <c>v0.2.0</c>, <c>V0.2.0</c> and <c>" v0.2.0 "</c> are one version.
    /// Public because the store compares with it too: the rule has to be the same on both sides of the port,
    /// or a plan could be found by one caller and missed by the next.
    /// </summary>
    public static bool Same(string? left, string? right) =>
        StringComparer.OrdinalIgnoreCase.Equals(left?.Trim(), right?.Trim());
}

/// <summary>
/// The release plans of a project: <c>.aiko/release-plan.json</c>.
/// </summary>
/// <remarks>
/// Held most recently created first, which is how the file is written and how the list is read: the plan
/// somebody just made is the one they are looking for. The order is a property of the stored document rather
/// than a sort done at reading time - a sort over two plans made in the same second has no order to give.
/// </remarks>
public sealed record ReleasePlanDocument(
    int SchemaVersion,
    IReadOnlyList<ReleasePlan> Plans)
{
    /// <summary>The most recently created plan, or null when nothing is planned.</summary>
    public ReleasePlan? Latest => Plans.Count > 0 ? Plans[0] : null;

    /// <summary>
    /// The plan new cards flow into, or null when the project plans nothing. A plan whose version has been
    /// recorded cannot be it: work that appears after a version shipped belongs to the next one.
    /// </summary>
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
/// What a caller asks a plan to become: an intent rather than a document to overwrite, applied to the plan as
/// it stands under the project's lock, so two callers adding two cards cannot lose each other's write. A field
/// left null is left as it is, and <c>Notes</c> passed as an empty string clears the note.
/// </summary>
public sealed record ReleasePlanUpdate(
    string Version,
    string? SchemeId = null,
    IReadOnlyList<string>? AddCards = null,
    IReadOnlyList<string>? RemoveCards = null,
    bool? IsCurrent = null,
    string? Notes = null);

/// <summary>
/// The rules a release plan obeys: what it has to carry, and when it may not be changed at all. Stated once,
/// like the rules of <c>ReleaseRecords</c> and <c>CardCommands</c>: the tool, the endpoint and a spec read
/// them from one place instead of each remembering them.
/// </summary>
public static class ReleasePlans
{
    /// <summary>Rejects a plan that names nothing a reader could find again.</summary>
    /// <exception cref="ArgumentException">The version or the scheme is missing or unusable.</exception>
    public static void Validate(string? version, string? schemeId) =>
        // A plan is for a version that will be tagged, so it answers to the rule a release answers to, and
        // that rule is stated once, in the release vocabulary.
        ReleaseRecords.Validate(version, schemeId);

    /// <summary>The card list as a plan keeps it: trimmed, and without the blanks that name no card.</summary>
    public static IReadOnlyList<string> NormalizeCards(IReadOnlyList<string>? cards) =>
        ReleaseRecords.NormalizeCards(cards);

    /// <summary>
    /// The plan as the release history has it: a version that has been recorded makes the plan a fact, so it
    /// reads as released from the moment the release is recorded, not only once its plan was closed.
    /// </summary>
    /// <remarks>
    /// Stated here, once, because it is a rule about plans rather than about one reader: the plan of a recorded
    /// version is not work still ahead, is not the version new cards flow into and may not be changed. A plan
    /// that already carries a release time is left exactly as the document holds it - the reading never invents
    /// a moment.
    /// </remarks>
    public static ReleasePlan Reconcile(ReleasePlan plan, ReleaseDocument history)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(history);
        return !plan.IsReleased && history.Find(plan.Version) is { } record
            ? plan with { ReleasedAt = record.ReleasedAt, IsCurrent = false }
            : plan;
    }

    /// <summary>
    /// Every plan of <paramref name="document"/> as the release history has it, in the order the document
    /// holds them.
    /// </summary>
    public static ReleasePlanDocument Reconcile(ReleasePlanDocument document, ReleaseDocument history)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(history);
        return document with { Plans = [.. document.Plans.Select(plan => Reconcile(plan, history))] };
    }

    /// <summary>
    /// Why a version may not be planned at all, or null when it may be.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="RefuseUpdate(ReleasePlan?)"/> because it answers a different question: that one
    /// is about a plan of a recorded version, this one about a version recorded and never planned. Work that
    /// appears after a version shipped belongs to the next version, so a plan for a release that is already a
    /// fact would be a version nobody can change.
    /// </remarks>
    public static string? RefusePlanning(string version, ReleaseDocument history)
    {
        ArgumentNullException.ThrowIfNull(history);
        return history.Find(version) is { } record
            ? $"the version '{record.Version}' was recorded at {record.ReleasedAt:u}, so it is not planned: "
              + "a released version is a fact and takes no work. Plan this work in another version instead."
            : null;
    }

    /// <summary>
    /// Why the plan may not be changed, or null when it may be. A plan whose version was recorded is closed:
    /// it is the answer to "what was this version waiting for", and a change made afterwards would make that
    /// answer untrue. There is no reopening - the next version gets its own plan.
    /// </summary>
    public static string? RefuseUpdate(ReleasePlan? plan) =>
        plan is { ReleasedAt: { } releasedAt }
            ? $"release plan '{plan.Version}' is closed: the version was recorded at {releasedAt:u}. A closed "
              + "plan says what that version was waiting for, so plan this work in another version instead."
            : null;
}
