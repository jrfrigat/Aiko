namespace Aiko.Domain.Cards;

/// <summary>
/// Well-known card types of a project board.
/// </summary>
/// <remarks>
/// A card type used to be a closed enum with two members, which made <c>Story</c> and <c>Task</c> the only
/// kinds a project could ever hold. A type is really the workflow a card moves through, and workflows are
/// project data the user edits - the built-in template just happens to ship two of them. So the type is a
/// string id here, and these constants name the ones Aiko is ready to work with: a project that declares a
/// workflow called <c>epic</c> gets cards of the type <c>Epic</c> without a code change, and the two built-in
/// ids keep their historical spelling so cards and templates written before this change still load.
/// </remarks>
public static class CardKind
{
    /// <summary>User story: a large functional requirement decomposed into child tasks.</summary>
    public const string Story = "Story";

    /// <summary>Atomic task executed by an agent within a single workflow stage.</summary>
    public const string Task = "Task";

    /// <summary>The types every installation has, in the order a form should offer them.</summary>
    public static IReadOnlyList<string> WellKnown { get; } = [Task, Story];

    /// <summary>Whether a type is one of the built-in ones (case-insensitive).</summary>
    /// <param name="kind">Type id to test.</param>
    public static bool IsWellKnown(string? kind) =>
        !string.IsNullOrWhiteSpace(kind) &&
        (string.Equals(kind, Story, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(kind, Task, StringComparison.OrdinalIgnoreCase));

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
    /// The canonical spelling of a type id read from a file: a well-known type keeps its historical casing,
    /// any other id is title-cased so the same type always compares equal within a project.
    /// </summary>
    /// <param name="kind">Type id as stored.</param>
    public static string Canonical(string? kind)
    {
        var value = kind?.Trim() ?? string.Empty;
        return string.Equals(value, Story, StringComparison.OrdinalIgnoreCase) ? Story
            : string.Equals(value, Task, StringComparison.OrdinalIgnoreCase) ? Task
            : FromWorkflowId(value);
    }
}

