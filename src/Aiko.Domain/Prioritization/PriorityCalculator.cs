namespace Aiko.Domain.Prioritization;

/// <summary>
/// Effective priority computation for tasks, taking parent card values into account.
/// </summary>
public static class PriorityCalculator
{
    /// <summary>
    /// Version of the current prioritization formula recorded in <see cref="PrioritySnapshot"/>.
    /// Version 2 uses the effective (blended) priority of parents instead of their own value.
    /// Version 3 normalizes a criterion's value by its configured range and multiplies the card's own
    /// score by its size coefficient (ТЗ §10), so two snapshots computed by different versions are not
    /// comparable - which is exactly what the version is for. Version 4 reads a manual priority on the
    /// board's 0..100 scale, so it lands on the same 0..1 scale as a score instead of outranking every scored
    /// card.
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
    /// A card keeps its manually entered priority until its project defines criteria and the card carries
    /// values for them: an empty criterion set scores nothing, and returning zero there would drop every
    /// card of a project that never opted into criteria. The manual value is read on the 0..100 scale (see
    /// <see cref="ManualScore"/>), so both kinds of card rank on one scale. The size still applies in both
    /// cases, because the size is a statement about the card rather than about the formula. Values are read
    /// from files a person can edit, so a negative one is clamped rather than refused: one bad card must not
    /// take the whole board down.
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
        var parentValues = parentPriorities.Select(value => Math.Max(value, 0m)).ToArray();

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
    /// Computes a card's own priority from its per-criterion values and the project's criteria: each
    /// value is normalized by the criterion's own range, then the values are averaged by weight (ТЗ §10).
    /// </summary>
    /// <remarks>
    /// The average is divided by the sum of the weights of the criteria that have a value. The formula in
    /// the specification writes the sum alone, which assumes weights that are shares and add up to one; a
    /// project is free to type 1.4 and 0.9, and dividing keeps the score on the same 0..1 scale either way.
    /// A criterion whose range is empty scores nothing rather than dividing by zero: the settings screen
    /// refuses that range, and a hand-edited file must not take the board down.
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
