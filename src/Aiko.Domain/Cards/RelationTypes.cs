namespace Aiko.Domain.Cards;

/// <summary>
/// Well-known relation types between cards.
/// </summary>
public static class RelationTypes
{
    /// <summary>A task implements a story requirement.</summary>
    public const string Implements = "implements";

    /// <summary>Hierarchical parent-child edge.</summary>
    public const string ParentChild = "parent-child";

    /// <summary>Directed "blocks" edge; must not form cycles.</summary>
    public const string Blocks = "blocks";

    /// <summary>Symmetric "relates to" edge.</summary>
    public const string RelatesTo = "relates-to";
}
