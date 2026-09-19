using Aiko.Domain.Prioritization;
using Flare.Components;
using Flare.Icons;

namespace Aiko.Pwa.Services;

/// <summary>
/// Maps the neutral appearance ids a workflow stores to the icon set and the Flare colours the cockpit
/// draws with.
/// </summary>
/// <remarks>
/// The ids live in the domain (<c>AppearanceCatalog</c>) so the same document can be validated by the daemon
/// and rendered by the client without either depending on the other. This is the one place that knows which
/// Flare member an id means, and every lookup carries a fallback: a project opened by an older build, or a
/// document hand-edited to an id this build does not know, still draws something instead of failing.
/// </remarks>
public static class CardAppearance
{
    /// <summary>Resolves an icon id, or returns the fallback when there is no choice or no match.</summary>
    /// <param name="icon">Icon id stored on a stage or a workflow.</param>
    /// <param name="fallback">
    /// Icon to draw when the id names nothing. Null by default, because "no choice" means no icon: a screen
    /// that wants the design's own iconography passes one explicitly.
    /// </param>
    public static FlareIcon? Icon(string? icon, FlareIcon? fallback = null) => icon switch
    {
        "inbox" => FlareIcons.Inbox,
        "description" => FlareIcons.Description,
        "code" => FlareIcons.Code,
        "check-circle" => FlareIcons.CheckCircle,
        "done-all" => FlareIcons.DoneAll,
        "play-arrow" => FlareIcons.PlayArrow,
        "pending" => MaterialDesign3Icons.Regular.Pending,
        "layers" => MaterialDesign3Icons.Regular.Layers,
        "target" => MaterialDesign3Icons.Regular.Target,
        "account-tree" => MaterialDesign3Icons.Regular.AccountTree,
        _ => fallback
    };

    /// <summary>Resolves a colour id, or returns the fallback when there is no choice or no match.</summary>
    /// <param name="color">Colour id stored on a stage or a workflow.</param>
    /// <param name="fallback">
    /// Colour to draw when the id names nothing. Null by default, which lets the theme's own colour through
    /// rather than inventing an accent the project never chose.
    /// </param>
    public static FlareColor? Color(string? color, FlareColor? fallback = null) => color switch
    {
        "primary" => FlareColor.Primary,
        "secondary" => FlareColor.Secondary,
        "tertiary" => FlareColor.Tertiary,
        "success" => FlareColor.Success,
        "warning" => FlareColor.Warning,
        "error" => FlareColor.Error,
        "info" => FlareColor.Info,
        "neutral" => FlareColor.OnSurfaceVariant,
        _ => fallback
    };

    /// <summary>
    /// The tone a size step carries: the direction its coefficient points in.
    /// </summary>
    /// <remarks>
    /// A step stores no colour on purpose - a step <em>is</em> its coefficient, and the tone is what that
    /// coefficient means at a glance. The settings ladder and a card's size badge both read this one rule, so
    /// the two cannot disagree about which step is the cheap one.
    /// </remarks>
    public static string SizeTone(decimal coefficient) => coefficient switch
    {
        > 1m => "up",
        < 1m => "down",
        _ => "mid"
    };

    /// <summary>The tag class a card's size badge is drawn with, one per tone.</summary>
    public static string SizeTagClass(decimal coefficient) => $"aiko-tag--size-{SizeTone(coefficient)}";

    /// <summary>
    /// The tag class a card's size badge carries, resolved from the project's grid: the tone of the step the
    /// card names, or the quiet one.
    /// </summary>
    /// <remarks>
    /// A tone is a statement about a coefficient, so without the step there is nothing to state: a card with no
    /// size, an empty grid, and a size whose step has left the grid all keep the neutral badge rather than
    /// borrowing a colour. The board card and the card's own banner both ask this, so the two screens cannot
    /// disagree about which step is the cheap one.
    /// </remarks>
    public static string SizeBadgeClass(string? size, IReadOnlyList<SizeDefinition> grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var step = grid.FirstOrDefault(item => StringComparer.Ordinal.Equals(item.Id, size));
        return step is null ? "aiko-tag--quiet" : SizeTagClass(step.Coefficient);
    }
}
