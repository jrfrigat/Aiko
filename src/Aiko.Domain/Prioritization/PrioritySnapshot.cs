namespace Aiko.Domain.Prioritization;

/// <summary>
/// One snapshot of a task's priority, recorded with the formula version that computed it.
/// </summary>
public sealed record PrioritySnapshot(
    decimal OwnPriority,
    decimal? MaximumParentPriority,
    decimal EffectivePriority,
    string FormulaVersion);
