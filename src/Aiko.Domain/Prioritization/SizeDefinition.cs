namespace Aiko.Domain.Prioritization;

/// <summary>
/// One step of a project's size grid (T-shirt sizing).
/// </summary>
/// <remarks>
/// The grid is data rather than an enum, because the project owns it: a step can be added, renamed, or given
/// another coefficient. The description is part of the model rather than a comment, because the agent assigns
/// the size and the table is what it reasons from.
/// </remarks>
public sealed record SizeDefinition
{
    /// <summary>
    /// Creates a step; the identifier must be present and the coefficient positive, because zero or negative
    /// would silently erase a card from the ranking.
    /// </summary>
    /// <param name="id">Stable identifier, for example <c>S</c>. Stored on the card that carries the step.</param>
    /// <param name="title">Display name.</param>
    /// <param name="description">What the step means, in the terms the agent can judge a card by.</param>
    /// <param name="coefficient">Multiplier the step applies to the card's own score; 1 is neutral.</param>
    public SizeDefinition(string id, string title, string description, decimal coefficient)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(coefficient);
        Id = id;
        Title = title;
        Description = description;
        Coefficient = coefficient;
    }

    /// <summary>Stable identifier of the step.</summary>
    public string Id { get; }

    /// <summary>Display name.</summary>
    public string Title { get; }

    /// <summary>What the step means.</summary>
    public string Description { get; }

    /// <summary>Multiplier applied to a card of this step.</summary>
    public decimal Coefficient { get; }
}
