namespace Aiko.Domain.Workflow;

/// <summary>
/// The curated appearance vocabulary of a workflow and its stages: which icon and which colour a status
/// column or a card type may carry.
/// </summary>
/// <remarks>
/// The ids here are deliberately neutral strings, not Flare members: the domain has no UI dependency, and the
/// same document is written by the daemon, read by the templates and rendered by the PWA. The client maps an
/// id to its own icon set and to <c>FlareColor</c>; anything outside this list is rejected when a workflow is
/// saved, so a hand-edited file cannot put an unknown name in front of a renderer that has no fallback.
/// </remarks>
public static class AppearanceCatalog
{
    /// <summary>The ten icons a status column or a card type may choose from.</summary>
    public static IReadOnlyList<string> Icons { get; } =
    [
        "inbox",
        "description",
        "code",
        "check-circle",
        "done-all",
        "play-arrow",
        "pending",
        "layers",
        "target",
        "account-tree"
    ];

    /// <summary>The accent colours a status column or a card type may choose from.</summary>
    public static IReadOnlyList<string> Colors { get; } =
    [
        "primary",
        "secondary",
        "tertiary",
        "success",
        "warning",
        "error",
        "info",
        "neutral"
    ];

    /// <summary>Whether an icon id is empty (no choice) or one of <see cref="Icons"/>.</summary>
    /// <param name="icon">Icon id to test.</param>
    public static bool IsValidIcon(string? icon) =>
        string.IsNullOrWhiteSpace(icon) || Icons.Contains(icon, StringComparer.Ordinal);

    /// <summary>Whether a colour id is empty (no choice) or one of <see cref="Colors"/>.</summary>
    /// <param name="color">Colour id to test.</param>
    public static bool IsValidColor(string? color) =>
        string.IsNullOrWhiteSpace(color) || Colors.Contains(color, StringComparer.Ordinal);

    /// <summary>Trims an icon id and turns "no choice" into one representation: null.</summary>
    /// <param name="icon">Icon id as submitted.</param>
    public static string? NormalizeIcon(string? icon) =>
        string.IsNullOrWhiteSpace(icon) ? null : icon.Trim();

    /// <summary>Trims a colour id and turns "no choice" into one representation: null.</summary>
    /// <param name="color">Colour id as submitted.</param>
    public static string? NormalizeColor(string? color) =>
        string.IsNullOrWhiteSpace(color) ? null : color.Trim();
}
