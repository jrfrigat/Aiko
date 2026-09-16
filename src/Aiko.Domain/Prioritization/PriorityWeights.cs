namespace Aiko.Domain.Prioritization;

/// <summary>
/// Weights of the own and parent value contributions to a task's effective priority.
/// </summary>
public sealed record PriorityWeights
{
    /// <summary>
    /// Default weights: 70% own value, 30% maximum parent value.
    /// </summary>
    public static PriorityWeights Default { get; } = new(0.7m, 0.3m);

    /// <summary>
    /// Creates weights; both values are non-negative and at least one is greater than zero.
    /// </summary>
    public PriorityWeights(decimal taskWeight, decimal parentWeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(taskWeight);
        ArgumentOutOfRangeException.ThrowIfNegative(parentWeight);

        if (taskWeight + parentWeight == 0)
        {
            throw new ArgumentException("At least one priority weight must be greater than zero.");
        }

        TaskWeight = taskWeight;
        ParentWeight = parentWeight;
    }

    /// <summary>
    /// Weight of the task's own value.
    /// </summary>
    public decimal TaskWeight { get; }

    /// <summary>
    /// Weight of the maximum parent value.
    /// </summary>
    public decimal ParentWeight { get; }
}
