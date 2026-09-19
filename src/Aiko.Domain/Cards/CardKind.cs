namespace Aiko.Domain.Cards;

/// <summary>
/// The shapes a card type id takes as it moves between a workflow id, a stored value and a label.
/// </summary>
/// <remarks>
/// A card type is not something the engine knows: it is the workflow a project declares, and the type id is
/// that workflow's id. This helper therefore only reshapes names - it never lists them. The built-in template
/// ships workflows called <c>epic</c>, <c>story</c> and <c>task</c>, but a project that declares none of them
/// works exactly the same.
/// </remarks>
public static class CardKind
{

    /// <summary>
    /// The type a workflow defines. The workflow id is the type id, so the two cannot drift: a workflow
    /// called <c>epic</c> defines <c>Epic</c>, exactly as <c>story</c> defines <c>Story</c>.
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
    /// The canonical spelling of a type id read from a file or a request: lower-cased and then capitalised,
    /// so the same type always compares equal within a project however a caller spelled it.
    /// </summary>
    /// <param name="kind">Type id as stored.</param>
    public static string Canonical(string? kind) => FromWorkflowId(ToWorkflowId(kind ?? string.Empty));
}

