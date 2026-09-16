namespace Aiko.Domain.Prioritization;

/// <summary>
/// A named scoring criterion of a project's priority formula.
/// </summary>
public sealed record PriorityCriterion(
    string Id,
    string Title,
    string Description,
    decimal Weight);

/// <summary>
/// Project priority configuration: the parent-blending weights and the list of
/// scoring criteria used to compute a card's own priority.
/// </summary>
public sealed record PrioritySettings(
    PriorityWeights Weights,
    IReadOnlyList<PriorityCriterion> Criteria)
{
    /// <summary>
    /// Safe default: default blending weights and no criteria (a card uses its own priority).
    /// </summary>
    public static PrioritySettings SafeDefault { get; } = new(PriorityWeights.Default, []);
}