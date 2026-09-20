using Flare.Abstractions.Tokens;

namespace Aiko.Theme.StitchFlow;

/// <summary>
/// The Aiko palette: a deep navy-indigo cockpit with a periwinkle accent.
/// </summary>
/// <remarks>
/// The dark scheme is the design's own, value for value. The design uses six surface planes, and Flare
/// 0.38 added the sixth role - <see cref="ColorScheme.SurfaceContainerLowest"/> - so the darkest plane
/// (the app bar and the project rail in the design) is named by it instead of borrowing
/// <see cref="ColorScheme.Background"/>, the colour of the document, as a substitute for a panel. The
/// canvas is <see cref="ColorScheme.Surface"/>. In this design the two carry the same value, which is why
/// naming the role properly moves nothing on screen.
/// The design defines no separate success/warning/info hues, so Success and Info deliberately reuse
/// the tertiary green and the secondary blue; Warning is the one role the design does not name, and
/// it is set to an amber that still belongs to the palette.
/// </remarks>
public static class StitchFlowPalettes
{
    /// <summary>Stable palette id - use this constant to select the palette without a magic string.</summary>
    public const string KineticId = "stitchflow-kinetic";

    /// <summary>Where these palettes come from, for palette pickers.</summary>
    public const string SourceName = "Aiko";

    // The two schemes are declared before the palette that packages them: static initializers run in
    // declaration order, and a palette built above them would capture nulls.
    private static readonly ColorScheme Dark = new()
    {
        // Accent: periwinkle primary, sky-blue secondary, mint tertiary.
        Primary = "#c0c1ff",
        OnPrimary = "#1000a9",
        PrimaryContainer = "#8083ff",
        OnPrimaryContainer = "#0d0096",
        Secondary = "#89ceff",
        OnSecondary = "#00344d",
        SecondaryContainer = "#00a2e6",
        OnSecondaryContainer = "#00344e",
        Tertiary = "#4edea3",
        OnTertiary = "#003824",
        TertiaryContainer = "#00885d",
        OnTertiaryContainer = "#000703",
        // State: a soft coral error, the palette's own amber warning. Success and info mirror
        // tertiary and secondary, because the design speaks of exactly three accents.
        Error = "#ffb4ab",
        OnError = "#690005",
        ErrorContainer = "#93000a",
        OnErrorContainer = "#ffdad6",
        Success = "#4edea3",
        OnSuccess = "#003824",
        SuccessContainer = "#00885d",
        OnSuccessContainer = "#000703",
        Warning = "#f2c46d",
        OnWarning = "#3a2a00",
        WarningContainer = "#5c4200",
        OnWarningContainer = "#ffdfa8",
        Info = "#89ceff",
        OnInfo = "#00344d",
        InfoContainer = "#00a2e6",
        OnInfoContainer = "#00344e",
        // Surfaces: six planes - the darkest is the chrome (app bar, rail), Surface is the canvas.
        Surface = "#0b1326",
        OnSurface = "#dae2fd",
        SurfaceVariant = "#2d3449",
        OnSurfaceVariant = "#c7c4d7",
        OnSurfaceVariant2 = "#8e91a8",
        SurfaceContainerLowest = "#060e20",
        SurfaceContainer = "#171f33",
        SurfaceContainerLow = "#131b2e",
        SurfaceContainerHigh = "#222a3d",
        SurfaceContainerHighest = "#2d3449",
        Background = "#060e20",
        OnBackground = "#dae2fd",
        Outline = "#908fa0",
        OutlineVariant = "#464554",
        InverseSurface = "#dae2fd",
        InverseOnSurface = "#283044",
        InversePrimary = "#494bd6",
        Scrim = "#000000",
        Shadow = "#000000",
        ShadowUmbra = "rgba(0, 0, 0, 0.6)",
        ShadowPenumbra = "rgba(0, 0, 0, 0.3)",
    };

    // The design is dark-only; this is the same role contract read in daylight - the accents deepen
    // so they stay legible on light planes, and the surface ladder inverts.
    private static readonly ColorScheme Light = new()
    {
        Primary = "#4a4bc4",
        OnPrimary = "#ffffff",
        PrimaryContainer = "#e0e0ff",
        OnPrimaryContainer = "#0d0096",
        Secondary = "#00658f",
        OnSecondary = "#ffffff",
        SecondaryContainer = "#c9e6ff",
        OnSecondaryContainer = "#001e2f",
        Tertiary = "#006b52",
        OnTertiary = "#ffffff",
        TertiaryContainer = "#6ffbbe",
        OnTertiaryContainer = "#002113",
        Error = "#ba1a1a",
        OnError = "#ffffff",
        ErrorContainer = "#ffdad6",
        OnErrorContainer = "#410002",
        Success = "#006b52",
        OnSuccess = "#ffffff",
        SuccessContainer = "#6ffbbe",
        OnSuccessContainer = "#002113",
        Warning = "#7a5900",
        OnWarning = "#ffffff",
        WarningContainer = "#ffdfa8",
        OnWarningContainer = "#3a2a00",
        Info = "#00658f",
        OnInfo = "#ffffff",
        InfoContainer = "#c9e6ff",
        OnInfoContainer = "#001e2f",
        Surface = "#f7f8fc",
        OnSurface = "#1b1b22",
        SurfaceVariant = "#e2e3ee",
        OnSurfaceVariant = "#45464f",
        OnSurfaceVariant2 = "#757780",
        // The ladder inverts with the scheme: the chrome plane is the brightest one here.
        SurfaceContainerLowest = "#ffffff",
        SurfaceContainer = "#eceef7",
        SurfaceContainerLow = "#f2f3fa",
        SurfaceContainerHigh = "#e5e7f1",
        SurfaceContainerHighest = "#dee1ec",
        Background = "#ffffff",
        OnBackground = "#1b1b22",
        Outline = "#757780",
        OutlineVariant = "#c5c6d0",
        InverseSurface = "#2f3038",
        InverseOnSurface = "#f1f0f9",
        InversePrimary = "#c0c1ff",
        Scrim = "#000000",
        Shadow = "#000000",
        ShadowUmbra = "rgba(0, 0, 0, 0.3)",
        ShadowPenumbra = "rgba(0, 0, 0, 0.15)",
    };

    /// <summary>The design's own dark scheme, with a light counterpart built from the same roles.</summary>
    public static readonly Palette Kinetic = new()
    {
        Id = KineticId,
        Name = "Kinetic Orchestration",
        Source = SourceName,
        Light = Light,
        Dark = Dark,
    };

    /// <summary>Every palette this theme ships.</summary>
    public static IReadOnlyList<Palette> All => [Kinetic];
}
