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
    /// <param name="fallback">Icon to draw when the id names nothing.</param>
    public static FlareIcon Icon(string? icon, FlareIcon fallback) => icon switch
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
    /// <param name="fallback">Colour to draw when the id names nothing.</param>
    public static FlareColor Color(string? color, FlareColor fallback) => color switch
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
}
