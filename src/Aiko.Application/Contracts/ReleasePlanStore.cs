namespace Aiko.Application.Contracts;

/// <summary>
/// The planned counterpart of the release history: what each version is waiting for.
/// </summary>
/// <remarks>
/// A document beside the project's other documents rather than a projection of the database, for the reason
/// the release history is one: it holds the answer to "what was v0.2.0 waiting for", which nothing else in
/// Aiko knows, and rebuilding the database must not be able to lose it.
/// <para>
/// Every mutation is stated as an intent and applied inside the store's per-project lock, because the only
/// correct read-modify-write is the one nobody outside can interleave with. There is no "save this
/// document" method on purpose: a caller that read a document and wrote it back could lose a change made in
/// between, which is exactly the race a plan shared by several agents runs into.
/// </para>
/// </remarks>
public interface IReleasePlanStore
{
    /// <summary>
    /// Reads the project's release plans, most recently created first.
    /// </summary>
    /// <param name="projectId">Project to read, by id or by its readable handle.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<ReleasePlanDocument> ReadAsync(string projectId, CancellationToken cancellationToken);

    /// <summary>
    /// Applies a change to a version's plan — creating it when it does not exist — and returns the plan as
    /// it was stored.
    /// </summary>
    /// <param name="projectId">Project to write to.</param>
    /// <param name="update">What the caller wants the plan to become.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentException">The version is not a tag, or the plan is new and names no scheme.</exception>
    /// <exception cref="InvalidOperationException">The plan is closed because its version was released.</exception>
    ValueTask<ReleasePlan> UpdateAsync(
        string projectId,
        ReleasePlanUpdate update,
        CancellationToken cancellationToken);

    /// <summary>
    /// Closes the plan of a version that has been released, and carries the work that outlived it forward.
    /// </summary>
    /// <remarks>
    /// Closing does not invent a version: the cards go into the plan named by
    /// <paramref name="carryIntoVersion"/> when it is given, otherwise into the next unreleased plan the
    /// project holds. When there is nowhere for them to go they stay on the closed plan, still marked with
    /// the version they came from, and it is for the person to say what version takes them.
    /// </remarks>
    /// <param name="projectId">Project to write to.</param>
    /// <param name="version">Version whose plan is being closed.</param>
    /// <param name="cardsToCarry">
    /// Cards of the plan that did not finish. An empty list is a statement rather than an omission: it says
    /// the version shipped everything it planned.
    /// </param>
    /// <param name="carryIntoVersion">Version that takes the unfinished cards, or null to let the store choose.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The plan as it was closed.</returns>
    /// <exception cref="ArgumentNullException">
    /// The caller named a version for the carried work whose plan is already closed.
    /// </exception>
    /// <exception cref="InvalidOperationException">No plan exists for the version, or it is already closed.</exception>
    ValueTask<ReleasePlan> CloseAsync(
        string projectId,
        string version,
        IReadOnlyList<string> cardsToCarry,
        string? carryIntoVersion,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes the plan of a version that will not be released.
    /// </summary>
    /// <remarks>
    /// A released version keeps its plan: it is the answer to what that version was waiting for. An unshipped
    /// plan is not history yet, so it may be removed when the person decides the version will not happen. Its
    /// cards are cards, not plan entries: they are never deleted. When the caller names a version they move
    /// there and say which plan they came from; otherwise they simply leave the plan.
    /// </remarks>
    /// <param name="projectId">Project to write to.</param>
    /// <param name="version">Version whose plan is being removed.</param>
    /// <param name="carryIntoVersion">Version that takes the plan's cards, or null to let them leave.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What was removed and where its cards went.</returns>
    /// <exception cref="KeyNotFoundException">No plan exists for the version.</exception>
    /// <exception cref="ArgumentException">The version was recorded, or the carry target is not usable.</exception>
    ValueTask<ReleasePlanDeletion> DeleteAsync(
        string projectId,
        string version,
        string? carryIntoVersion,
        CancellationToken cancellationToken);
}
