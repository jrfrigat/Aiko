namespace Aiko.Domain.Prioritization;

/// <summary>
/// Effective priority computation for tasks, taking parent card values into account.
/// </summary>
public static class PriorityCalculator
{
    /// <summary>
    /// Version of the current prioritization formula recorded in <see cref="PrioritySnapshot"/>: 2 blends
    /// parents by their own effective priority, 3 normalizes a criterion by its configured range and applies
    /// the size coefficient, 4 reads a manual priority on the board's 0..100 scale. Two snapshots from
    /// different versions are not comparable, which is what the version is for.
    /// </summary>
    public const string CurrentFormulaVersion = "4";

    /// <summary>
    /// The top of the scale a manual priority is typed on: the board prints every priority as 0..100, so a
    /// person who types 25 means what the board shows as 25.
    /// </summary>
    public const decimal ManualScale = 100m;

    /// <summary>
    /// A manual priority as a score: its place on the 0..100 scale as a fraction, clamped to the scale - a
    /// negative value is nothing, and a value above the scale is the top of it.
    /// </summary>
    /// <param name="manualPriority">The priority a person or an agent typed.</param>
    public static decimal ManualScore(decimal manualPriority) =>
        Math.Clamp(manualPriority, 0m, ManualScale) / ManualScale;

    /// <summary>
    /// A card's own score: the weighted average of its normalized criterion values, multiplied by the
    /// coefficient of its size step.
    /// </summary>
    /// <remarks>
    /// A card keeps its manually entered priority until its project defines criteria and the card carries values
    /// for them: an empty criterion set scores nothing, and returning zero there would drop every card of a
    /// project that never opted into criteria. The size applies either way, because the size is a statement
    /// about the card rather than about the formula, and a value read from a file that a person can edit is
    /// clamped rather than refused.
    /// </remarks>
    /// <param name="manualPriority">The card's own priority as stored.</param>
    /// <param name="criterionValues">Per-criterion values on the card, keyed by criterion id.</param>
    /// <param name="settings">The project's priority settings: criteria and the size grid.</param>
    /// <param name="sizeId">The card's size step, or null.</param>
    public static decimal CalculateOwnScore(
        decimal manualPriority,
        IReadOnlyDictionary<string, decimal>? criterionValues,
        PrioritySettings settings,
        string? sizeId)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var ownPriority = criterionValues is { Count: > 0 } && settings.Criteria.Count > 0
            ? CalculateOwnPriority(criterionValues, settings.Criteria)
            : ManualScore(manualPriority);

        return ownPriority * settings.SizeFactor(sizeId);
    }

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

        // Clamped rather than refused, for the same reason as the own score: these come from files.
        ownPriority = Math.Max(ownPriority, 0m);

        // One pass, no intermediate array: the board computes this for every card, and the values only
        // need a count and the largest one.
        var parentCount = 0;
        var maximumParent = 0m;
        foreach (var value in parentPriorities)
        {
            Accumulate(value, ref parentCount, ref maximumParent);
        }

        return Compose(ownPriority, parentCount, maximumParent, weights);
    }

    /// <summary>
    /// Computes a task priority from parents the caller already holds in a list, so the board does not box an
    /// enumerator for every card.
    /// </summary>
    /// <param name="ownPriority">The card's own score.</param>
    /// <param name="parentPriorities">The effective priorities of its parents.</param>
    /// <param name="weights">The blending weights, or the default when absent.</param>
    public static PrioritySnapshot CalculateTask(
        decimal ownPriority,
        IReadOnlyList<decimal> parentPriorities,
        PriorityWeights? weights = null)
    {
        ArgumentNullException.ThrowIfNull(parentPriorities);

        ownPriority = Math.Max(ownPriority, 0m);

        var maximumParent = 0m;
        for (var index = 0; index < parentPriorities.Count; index++)
        {
            var clamped = Math.Max(parentPriorities[index], 0m);
            if (index == 0 || clamped > maximumParent)
            {
                maximumParent = clamped;
            }
        }

        return Compose(ownPriority, parentPriorities.Count, maximumParent, weights);
    }

    /// <summary>
    /// Computes a task priority from the largest parent priority the caller already selected.
    /// </summary>
    /// <remarks>
    /// The board projects every card in one pass and only needs the largest parent value, so it does not
    /// build a list per card. A value read from a file is clamped rather than refused.
    /// </remarks>
    /// <param name="ownPriority">The card's own score.</param>
    /// <param name="maximumParentPriority">The largest effective priority of its parents, or null when it has none.</param>
    /// <param name="weights">The blending weights, or the default when absent.</param>
    public static PrioritySnapshot CalculateTaskFromMaximum(
        decimal ownPriority,
        decimal? maximumParentPriority,
        PriorityWeights? weights = null)
    {
        ownPriority = Math.Max(ownPriority, 0m);
        return maximumParentPriority is { } maximum
            ? Compose(ownPriority, 1, Math.Max(maximum, 0m), weights)
            : Compose(ownPriority, 0, 0m, weights);
    }

    private static void Accumulate(decimal value, ref int count, ref decimal maximum)
    {
        var clamped = Math.Max(value, 0m);
        if (count == 0 || clamped > maximum)
        {
            maximum = clamped;
        }

        count++;
    }

    private static PrioritySnapshot Compose(
        decimal ownPriority,
        int parentCount,
        decimal maximumParent,
        PriorityWeights? weights)
    {
        if (parentCount == 0)
        {
            return new(ownPriority, null, ownPriority, CurrentFormulaVersion);
        }

        var effectiveWeights = weights ?? PriorityWeights.Default;
        var effective =
            ((effectiveWeights.TaskWeight * ownPriority) +
             (effectiveWeights.ParentWeight * maximumParent)) /
            (effectiveWeights.TaskWeight + effectiveWeights.ParentWeight);

        return new(ownPriority, maximumParent, effective, CurrentFormulaVersion);
    }

    /// <summary>
    /// Computes a card's own priority from its per-criterion values and the project's criteria: each value is
    /// normalized by the criterion's own range, then the values are averaged by weight.
    /// </summary>
    /// <remarks>
    /// The average is divided by the sum of the weights that had a value. The specification writes the sum
    /// alone, which assumes weights that add up to one; a project is free to type 1.4 and 0.9, and dividing keeps
    /// the score on the same 0..1 scale either way. A criterion whose range is empty scores nothing rather than
    /// dividing by zero.
    /// </remarks>
    /// <param name="values">Values keyed by criterion id.</param>
    /// <param name="criteria">The criteria of the project.</param>
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
            if (!values.TryGetValue(criterion.Id, out var value))
            {
                continue;
            }

            var span = criterion.Maximum - criterion.Minimum;
            if (span <= 0m || criterion.Weight <= 0m)
            {
                continue;
            }

            var normalized = Math.Clamp((value - criterion.Minimum) / span, 0m, 1m);
            total += normalized * criterion.Weight;
            weightSum += criterion.Weight;
        }

        return weightSum == 0 ? 0m : total / weightSum;
    }
}
