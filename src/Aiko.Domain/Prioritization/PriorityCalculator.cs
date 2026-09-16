namespace Aiko.Domain.Prioritization;

/// <summary>
/// Effective priority computation for tasks, taking parent card values into account.
/// </summary>
public static class PriorityCalculator
{
    /// <summary>
    /// Version of the current prioritization formula recorded in <see cref="PrioritySnapshot"/>.
    /// Version 2 uses the effective (blended) priority of parents instead of their own value.
    /// </summary>
    public const string CurrentFormulaVersion = "2";

    /// <summary>
    /// Computes a task priority. Without parents the effective priority equals the own value;
    /// otherwise it is the weighted average of the own priority and the maximum parent priority.
    /// </summary>
    public static PrioritySnapshot CalculateTask(
        decimal ownPriority,
        IEnumerable<decimal> parentPriorities,
        PriorityWeights? weights = null)
    {
        ArgumentNullException.ThrowIfNull(parentPriorities);
        ArgumentOutOfRangeException.ThrowIfNegative(ownPriority);

        var parentValues = parentPriorities.ToArray();
        if (parentValues.Any(value => value < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(parentPriorities));
        }

        if (parentValues.Length == 0)
        {
            return new(ownPriority, null, ownPriority, CurrentFormulaVersion);
        }

        var effectiveWeights = weights ?? PriorityWeights.Default;
        var maximumParent = parentValues.Max();
        var effective =
            ((effectiveWeights.TaskWeight * ownPriority) +
             (effectiveWeights.ParentWeight * maximumParent)) /
            (effectiveWeights.TaskWeight + effectiveWeights.ParentWeight);

        return new(ownPriority, maximumParent, effective, CurrentFormulaVersion);
    }

    /// <summary>
    /// Computes a card's own priority from its per-criterion values and the project's
    /// criteria: a weighted average over the criteria that have a value. With no criteria
    /// the raw values are summed as a fallback.
    /// </summary>
    public static decimal CalculateOwnPriority(
        IReadOnlyDictionary<string, decimal> values,
        IReadOnlyList<PriorityCriterion> criteria)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(criteria);
        if (criteria.Count == 0)
        {
            return values.Values.Sum();
        }

        var total = 0m;
        var weightSum = 0m;
        foreach (var criterion in criteria)
        {
            if (values.TryGetValue(criterion.Id, out var value))
            {
                total += value * criterion.Weight;
                weightSum += criterion.Weight;
            }
        }

        return weightSum == 0 ? 0m : total / weightSum;
    }
}
