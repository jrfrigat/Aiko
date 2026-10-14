namespace Aiko.Application.Contracts;

/// <summary>
/// The planned counterpart of the release history: what each version is waiting for. Projects are named by
/// id or by their readable handle.
/// </summary>
/// <remarks>
/// A document rather than a projection: nothing else knows what a version was waiting for, and rebuilding
/// the database must not be able to lose it. Every mutation is an intent applied inside the per-project
/// lock - there is no "save this document", so a caller cannot write back a stale copy.
/// </remarks>
public interface IReleasePlanStore
{
    /// <summary>Reads the project's release plans, most recently created first.</summary>
    ValueTask<ReleasePlanDocument> ReadAsync(string projectId, CancellationToken cancellationToken);

    /// <summary>Applies a change to a version's plan, creating it when it does not exist, and returns the
    /// plan as it was stored.</summary>
    /// <exception cref="ArgumentException">The version is not a tag, or the plan is new and names no scheme.</exception>
    /// <exception cref="InvalidOperationException">The plan is closed because its version was released.</exception>
    ValueTask<ReleasePlan> UpdateAsync(
        string projectId,
        ReleasePlanUpdate update,
        CancellationToken cancellationToken);

    /// <summary>Closes the plan of a released version and carries the work that outlived it forward.</summary>
    /// <remarks>
    /// Closing invents no version: the cards go into the plan named by <paramref name="carryIntoVersion"/>,
    /// otherwise into the next unreleased plan. With nowhere to go they stay on the closed plan, and it is
    /// for the person to say what version takes them.
    /// </remarks>
    /// <returns>The plan as it was closed; an empty <c>cardsToCarry</c> says the version shipped everything
    /// it planned.</returns>
    /// <exception cref="ArgumentException">The version named to carry work into is not a tag, or it was
    /// already released.</exception>
    /// <exception cref="InvalidOperationException">No plan exists for the version, or it is already closed.</exception>
    ValueTask<ReleasePlan> CloseAsync(
        string projectId,
        string version,
        IReadOnlyList<string> cardsToCarry,
        string? carryIntoVersion,
        CancellationToken cancellationToken);
}
