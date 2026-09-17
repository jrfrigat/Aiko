namespace Aiko.Domain.Prioritization;

/// <summary>
/// A named scoring criterion of a project's priority formula.
/// </summary>
/// <param name="Id">Stable identifier the card's criterion values are keyed by.</param>
/// <param name="Title">Display name.</param>
/// <param name="Description">What the criterion measures, for the person reading the settings.</param>
/// <param name="Weight">How much the criterion contributes to the card's own score.</param>
/// <param name="Minimum">Lowest value the criterion accepts; normalization maps it to zero.</param>
/// <param name="Maximum">Highest value the criterion accepts; normalization maps it to one.</param>
/// <param name="AiInstruction">
/// What the agent should do to score this criterion (ТЗ §10). The agent sets the value, so the
/// instruction is the only thing that makes the number mean anything.
/// </param>
public sealed record PriorityCriterion(
    string Id,
    string Title,
    string Description,
    decimal Weight,
    decimal Minimum = 0m,
    decimal Maximum = 1m,
    string? AiInstruction = null);

/// <summary>
/// Project priority configuration: the parent-blending weights, the scoring criteria used to compute a
/// card's own score, and the size grid that multiplies it.
/// </summary>
/// <param name="Weights">How a task blends its own score with its parents'.</param>
/// <param name="Criteria">The criteria a card is scored against.</param>
/// <param name="Sizes">
/// The project's size grid. Null on settings written before the grid existed, and on projects that never
/// configured one; an absent grid means every card's size factor is 1.
/// </param>
public sealed record PrioritySettings(
    PriorityWeights Weights,
    IReadOnlyList<PriorityCriterion> Criteria,
    IReadOnlyList<SizeDefinition>? Sizes = null)
{
    /// <summary>The standard T-shirt ladder: the small steps are worth more, a large card is a plan to split.</summary>
    // Declared before SafeDefault on purpose: static initializers run in declaration order, and a default
    // built above this one would capture a null grid.
    public static IReadOnlyList<SizeDefinition> DefaultGrid { get; } =
    [
        new("XS", "XS", "Under an hour: one file, no analysis, no new dependencies.", 1.15m),
        new("S", "S", "Half a day or less: a small change that a local test can verify.", 1.08m),
        new("M", "M", "About a day: a normal task with analysis and implementation.", 1.00m),
        new("L", "L", "Several days: decompose into subtasks and agree the order before starting.", 0.90m),
        new("XL", "XL", "Over a week: do not start until it is split into S and M tasks.", 0.80m),
    ];

    /// <summary>
    /// Safe default: default blending weights, no criteria and the standard T-shirt grid.
    /// </summary>
    /// <remarks>
    /// The grid is a built-in default rather than template content, for the same reason the weights are:
    /// it is the policy a project starts from when nothing is configured, and a settings file - global or
    /// project - replaces it by stating its own. The descriptions are what an agent reads to decide which
    /// step a card belongs to, so they say what the step means in work rather than in numbers.
    /// </remarks>
    public static PrioritySettings SafeDefault { get; } = new(PriorityWeights.Default, [], DefaultGrid);

    /// <summary>The configured size grid, or an empty one when these settings predate sizes.</summary>
    public IReadOnlyList<SizeDefinition> Grid => Sizes ?? [];

    /// <summary>
    /// The multiplier a card of this step gets. A card with no size, or with a step that is no longer in
    /// the grid (a renamed or removed step), is neutral rather than penalized.
    /// </summary>
    /// <param name="sizeId">The card's size step, or null.</param>
    public decimal SizeFactor(string? sizeId) =>
        string.IsNullOrWhiteSpace(sizeId)
            ? 1m
            : Grid.FirstOrDefault(size => StringComparer.Ordinal.Equals(size.Id, sizeId))?.Coefficient ?? 1m;
}