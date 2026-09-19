namespace Aiko.Domain.Execution;

/// <summary>
/// What a project allows an agent working in another project to do to it.
/// </summary>
public enum CrossProjectWritePolicy
{
    /// <summary>Nobody writes into this project from outside it: the card is created by hand instead.</summary>
    Deny,

    /// <summary>Every hand-over is confirmed with the user before the card is created.</summary>
    Ask,

    /// <summary>A card from a linked project is created without asking.</summary>
    Allow
}

/// <summary>
/// The cross-project write policy of a project: whether another project may create cards in it, and which
/// projects may.
/// </summary>
/// <remarks>
/// The policy belongs to the project that <em>sends</em> the work, not to the one that receives it: the sender
/// decides what may leave it, and the receiver only sees the origin mark on the card and decides what to do
/// with it. An empty target list means any registered project, still bounded by the policy.
/// </remarks>
/// <param name="WritePolicy">Whether a cross-project card may be created at all, and under what terms.</param>
/// <param name="AllowedTargetProjects">
/// Target project ids or readable handles this project may write to, or null/empty for any of them.
/// </param>
public sealed record CrossProjectSettings(
    CrossProjectWritePolicy WritePolicy = CrossProjectWritePolicy.Deny,
    IReadOnlyList<string>? AllowedTargetProjects = null)
{
    /// <summary>
    /// What a project that states nothing gets: writing from outside is refused.
    /// </summary>
    /// <remarks>
    /// Deny is the safe reading of silence: a project that never configured the hand-over has not agreed to it,
    /// and the refusal tells the agent to create the card by hand in the target.
    /// </remarks>
    public static CrossProjectSettings SafeDefault { get; } = new();

    /// <summary>The configured targets, or an empty list when any project is allowed.</summary>
    public IReadOnlyList<string> Targets => AllowedTargetProjects ?? [];
}
