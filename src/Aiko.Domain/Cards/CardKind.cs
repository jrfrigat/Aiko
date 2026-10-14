namespace Aiko.Domain.Cards;

/// <summary>
/// The shapes a card type id takes as it moves between a workflow id, a stored value and a label.
/// </summary>
/// <remarks>
/// A card type is not something the engine knows: it is the workflow a project declares. This helper therefore
/// only reshapes the id - it never lists types, and a project whose workflows have other names works the same.
/// </remarks>
public static class CardKind
{

    /// <summary>
    /// The type a workflow defines. The workflow id is the type id, so the two cannot drift.
    /// </summary>
    /// <param name="workflowId">Workflow id, for example <c>story</c>.</param>
    public static string FromWorkflowId(string workflowId)
    {
        var id = workflowId?.Trim() ?? string.Empty;
        return id.Length == 0
            ? string.Empty
            : char.ToUpperInvariant(id[0]) + id[1..];
    }

    /// <summary>The workflow id that defines a type - the type id, lower-cased.</summary>
    /// <param name="kind">Card type id, for example <c>Story</c>.</param>
    public static string ToWorkflowId(string kind) => (kind ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>
    /// The canonical spelling of a type id read from a file or a request, so the same type compares equal
    /// however a caller spelled it.
    /// </summary>
    /// <param name="kind">Type id as stored.</param>
    public static string Canonical(string? kind) => FromWorkflowId(ToWorkflowId(kind ?? string.Empty));
}

