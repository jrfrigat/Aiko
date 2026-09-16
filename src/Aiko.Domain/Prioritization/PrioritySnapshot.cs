namespace Aiko.Domain.Prioritization;

/// <summary>
/// A priority snapshot of a task: own value, maximum across parents
/// and the computed effective priority.
/// </summary>
public sealed record PrioritySnapshot(
    decimal OwnPriority,
    decimal? MaximumParentPriority,
    decimal EffectivePriority,
    string FormulaVersion);
