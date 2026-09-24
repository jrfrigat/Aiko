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

    /// <summary>Stable palette id of the warm amber sibling.</summary>
    public const string ParchmentId = "stitchflow-parchment";

    /// <summary>Stable palette id of the cool amber sibling.</summary>
    public const string OperatorId = "stitchflow-operator";

    /// <summary>Stable palette id of the editor-workstation sibling.</summary>
    public const string StudioModernId = "stitchflow-studio-modern";

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
        // Measured, not chosen by eye: #757780 reads 4.20 against this scheme's surface, under the 4.5
        // a text tone owes, and it stood here because nothing measured it. The third tone must stay
        // fainter than OnSurfaceVariant above - which reads 8.83 - so it is darkened just far enough.
        OnSurfaceVariant2 = "#6c6e77",
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

    // The two siblings below are one design family - "Cyber Amber" - carried warm and cool, and each
    // file below designs one scheme of one of them, so the roles it names are taken value for value:
    //   .claude/design/Palettes/2.md      -> Parchment light
    //   .claude/design/Palettes/2dark.md  -> Parchment dark
    //   .claude/design/Palettes/3.md      -> Operator light
    //   .claude/design/Palettes/3dark.md  -> Operator dark
    // Those files carry no Success, Warning, Info, OnSurfaceVariant2, Scrim or Shadow role, so those
    // are named here and the choice is deliberate:
    //   Success repeats the tertiary green and Warning the secondary accent. These designs speak of
    //     three accents plus error and name no separate success or warning hue, so borrowing the
    //     family is truer to them than inventing a fourth accent - the same borrowing this theme's
    //     own Kinetic palette already makes.
    //   Info is the one invented family, because not one of the four schemes has a blue. It is
    //     deliberately the same blue in both palettes: "information" should read the same whichever
    //     palette someone picks, and it is a status role rather than part of either identity.
    //   OnSurfaceVariant2 is the third, fainter tone the two-tone on-surface ramp lacks.
    // Every text/background pair here was measured before it was written down: the designs' own
    // container pairs sit at 4.53-4.59 (AA exactly), and nothing falls below 4.5 for text or 3.0 for
    // outlines and accents-on-surface. The values are not a starting point to tweak by eye.

    /// <summary>The warm amber scheme on a warm charcoal, as the design draws it.</summary>
    private static readonly ColorScheme ParchmentDark = new()
    {
        Primary = "#ffb77d",
        OnPrimary = "#4d2600",
        PrimaryContainer = "#d97707",
        OnPrimaryContainer = "#432100",
        Secondary = "#ffb68e",
        OnSecondary = "#532200",
        SecondaryContainer = "#ab4c00",
        OnSecondaryContainer = "#ffe2d5",
        Tertiary = "#68dba9",
        OnTertiary = "#003825",
        TertiaryContainer = "#25a475",
        OnTertiaryContainer = "#00311f",
        Error = "#ffb4ab",
        OnError = "#690005",
        ErrorContainer = "#93000a",
        OnErrorContainer = "#ffdad6",
        Success = "#68dba9",
        OnSuccess = "#003825",
        SuccessContainer = "#25a475",
        OnSuccessContainer = "#00311f",
        Warning = "#ffb68e",
        OnWarning = "#532200",
        WarningContainer = "#ab4c00",
        OnWarningContainer = "#ffe2d5",
        Info = "#8ecfff",
        OnInfo = "#00344d",
        InfoContainer = "#00639b",
        OnInfoContainer = "#cde5ff",
        Surface = "#161311",
        OnSurface = "#e9e1dd",
        SurfaceVariant = "#383432",
        OnSurfaceVariant = "#dbc2b0",
        OnSurfaceVariant2 = "#a08d7c",
        SurfaceContainer = "#221f1d",
        SurfaceContainerLowest = "#100e0c",
        SurfaceContainerLow = "#1e1b19",
        SurfaceContainerHigh = "#2d2927",
        SurfaceContainerHighest = "#383432",
        Background = "#161311",
        OnBackground = "#e9e1dd",
        Outline = "#a38c7c",
        OutlineVariant = "#554336",
        InverseSurface = "#e9e1dd",
        InverseOnSurface = "#33302d",
        InversePrimary = "#904d00",
        Scrim = "#000000",
        Shadow = "#000000",
        ShadowUmbra = "rgba(0, 0, 0, 0.6)",
        ShadowPenumbra = "rgba(0, 0, 0, 0.3)",
    };

    /// <summary>The same warm family read on parchment, as the design draws it.</summary>
    private static readonly ColorScheme ParchmentLight = new()
    {
        Primary = "#8d4b00",
        OnPrimary = "#ffffff",
        PrimaryContainer = "#b15f00",
        OnPrimaryContainer = "#fffbff",
        Secondary = "#9b4500",
        OnSecondary = "#ffffff",
        SecondaryContainer = "#fd8a42",
        OnSecondaryContainer = "#682c00",
        Tertiary = "#006948",
        OnTertiary = "#ffffff",
        TertiaryContainer = "#00855d",
        OnTertiaryContainer = "#f5fff7",
        Error = "#ba1a1a",
        OnError = "#ffffff",
        ErrorContainer = "#ffdad6",
        OnErrorContainer = "#93000a",
        Success = "#006948",
        OnSuccess = "#ffffff",
        SuccessContainer = "#00855d",
        OnSuccessContainer = "#f5fff7",
        Warning = "#9b4500",
        OnWarning = "#ffffff",
        WarningContainer = "#fd8a42",
        OnWarningContainer = "#682c00",
        Info = "#00658f",
        OnInfo = "#ffffff",
        InfoContainer = "#c9e6ff",
        OnInfoContainer = "#001e2f",
        Surface = "#fff8f5",
        OnSurface = "#1e1b19",
        SurfaceVariant = "#e9e1dd",
        OnSurfaceVariant = "#554336",
        OnSurfaceVariant2 = "#6d5c4d",
        SurfaceContainer = "#f4ece8",
        SurfaceContainerLowest = "#ffffff",
        SurfaceContainerLow = "#faf2ee",
        SurfaceContainerHigh = "#eee7e3",
        SurfaceContainerHighest = "#e9e1dd",
        Background = "#fff8f5",
        OnBackground = "#1e1b19",
        Outline = "#887364",
        OutlineVariant = "#dbc2b0",
        InverseSurface = "#33302d",
        InverseOnSurface = "#f7efeb",
        InversePrimary = "#ffb77d",
        Scrim = "#000000",
        Shadow = "#000000",
        ShadowUmbra = "rgba(0, 0, 0, 0.3)",
        ShadowPenumbra = "rgba(0, 0, 0, 0.15)",
    };

    /// <summary>The warm amber sibling: brown-amber accents on parchment and on warm charcoal.</summary>
    public static readonly Palette Parchment = new()
    {
        Id = ParchmentId,
        Name = "Cyber Amber Parchment",
        Source = SourceName,
        Light = ParchmentLight,
        Dark = ParchmentDark,
    };

    /// <summary>The amber scheme on cool operator chrome, as the design draws it.</summary>
    private static readonly ColorScheme OperatorDark = new()
    {
        Primary = "#ffc174",
        OnPrimary = "#472a00",
        PrimaryContainer = "#f59e0b",
        OnPrimaryContainer = "#613b00",
        Secondary = "#ffc640",
        OnSecondary = "#402d00",
        SecondaryContainer = "#e3aa00",
        OnSecondaryContainer = "#5a4100",
        Tertiary = "#4de6aa",
        OnTertiary = "#003825",
        TertiaryContainer = "#22c990",
        OnTertiaryContainer = "#004e35",
        Error = "#ffb4ab",
        OnError = "#690005",
        ErrorContainer = "#93000a",
        OnErrorContainer = "#ffdad6",
        Success = "#4de6aa",
        OnSuccess = "#003825",
        SuccessContainer = "#22c990",
        OnSuccessContainer = "#004e35",
        Warning = "#ffc640",
        OnWarning = "#402d00",
        WarningContainer = "#e3aa00",
        OnWarningContainer = "#5a4100",
        Info = "#8ecfff",
        OnInfo = "#00344d",
        InfoContainer = "#00639b",
        OnInfoContainer = "#cde5ff",
        Surface = "#0e131c",
        OnSurface = "#dee2ef",
        SurfaceVariant = "#30353f",
        OnSurfaceVariant = "#d8c3ad",
        OnSurfaceVariant2 = "#9c8f7d",
        SurfaceContainer = "#1b2029",
        SurfaceContainerLowest = "#090e17",
        SurfaceContainerLow = "#171c25",
        SurfaceContainerHigh = "#252a34",
        SurfaceContainerHighest = "#30353f",
        Background = "#0e131c",
        OnBackground = "#dee2ef",
        Outline = "#a08e7a",
        OutlineVariant = "#534434",
        InverseSurface = "#dee2ef",
        InverseOnSurface = "#2c303a",
        InversePrimary = "#855300",
        Scrim = "#000000",
        Shadow = "#000000",
        ShadowUmbra = "rgba(0, 0, 0, 0.6)",
        ShadowPenumbra = "rgba(0, 0, 0, 0.3)",
    };

    /// <summary>The same amber read on cool slate, as the design draws it.</summary>
    private static readonly ColorScheme OperatorLight = new()
    {
        Primary = "#855300",
        OnPrimary = "#ffffff",
        PrimaryContainer = "#f59e0b",
        OnPrimaryContainer = "#613b00",
        Secondary = "#795900",
        OnSecondary = "#ffffff",
        SecondaryContainer = "#ffc329",
        OnSecondaryContainer = "#6f5100",
        Tertiary = "#006c4b",
        OnTertiary = "#ffffff",
        TertiaryContainer = "#22c990",
        OnTertiaryContainer = "#004e35",
        Error = "#ba1a1a",
        OnError = "#ffffff",
        ErrorContainer = "#ffdad6",
        OnErrorContainer = "#93000a",
        Success = "#006c4b",
        OnSuccess = "#ffffff",
        SuccessContainer = "#22c990",
        OnSuccessContainer = "#004e35",
        Warning = "#795900",
        OnWarning = "#ffffff",
        WarningContainer = "#ffc329",
        OnWarningContainer = "#6f5100",
        Info = "#00658f",
        OnInfo = "#ffffff",
        InfoContainer = "#c9e6ff",
        OnInfoContainer = "#001e2f",
        Surface = "#f9f9ff",
        OnSurface = "#171c25",
        SurfaceVariant = "#dee2ef",
        OnSurfaceVariant = "#534434",
        OnSurfaceVariant2 = "#6b5c4d",
        SurfaceContainer = "#eaeefb",
        SurfaceContainerLowest = "#ffffff",
        SurfaceContainerLow = "#f0f3ff",
        SurfaceContainerHigh = "#e4e8f5",
        SurfaceContainerHighest = "#dee2ef",
        Background = "#f9f9ff",
        OnBackground = "#171c25",
        Outline = "#867461",
        OutlineVariant = "#d8c3ad",
        InverseSurface = "#2c303a",
        InverseOnSurface = "#ecf0fe",
        InversePrimary = "#ffb95f",
        Scrim = "#000000",
        Shadow = "#000000",
        ShadowUmbra = "rgba(0, 0, 0, 0.3)",
        ShadowPenumbra = "rgba(0, 0, 0, 0.15)",
    };

    /// <summary>The cool amber sibling: amber on slate chrome rather than on warm charcoal.</summary>
    public static readonly Palette Operator = new()
    {
        Id = OperatorId,
        Name = "Cyber Amber Operator",
        Source = SourceName,
        Light = OperatorLight,
        Dark = OperatorDark,
    };

    // The fourth sibling is the editor-workstation design the files 4 and 4dark carry, and each file
    // designs one scheme of it, so the roles it names are taken value for value:
    //   .claude/design/Palettes/4.md      -> Studio Modern light
    //   .claude/design/Palettes/4dark.md  -> Studio Modern dark
    // That design speaks of three accents - a blue primary, a teal-green secondary and a warm brown
    // tertiary - plus error, and names no success, warning, info or third on-surface tone of its own.
    // The roles it leaves out are filled the way the amber siblings fill theirs: by lending an accent's
    // whole quadruple rather than inventing a hue.
    //   Success borrows the teal-green secondary and Warning the warm tertiary - the same move the amber
    //     palettes make, where their one warm accent carries the warning.
    //   Info borrows the primary blue, because this design does have a blue. That is the one place it
    //     parts company with the amber family, whose Info had to be a shared colour because none of its
    //     four schemes named a blue at all.
    //   OnSurfaceVariant2 is the third, fainter tone the two-tone on-surface ramp lacks. On the dark
    //     scheme it is the design's own outline grey; on the light one that grey reads 4.26 against the
    //     surface - under the 4.5 a text tone owes - so it is darkened just far enough to clear it.
    // The design's roles that no ColorScheme role names - surface-dim, surface-bright, surface-tint and
    // the *-fixed ladders - are not carried across: ColorScheme has no seat for them.
    // Every text/background pair here was measured before it was written down: the design's own container
    // pairs sit at 4.54-4.57 (AA exactly), and nothing falls below 4.5 for text or 3.0 for outlines and
    // accents-on-surface. The values are not a starting point to tweak by eye.

    /// <summary>The editor-workstation scheme in daylight, as the design draws it.</summary>
    private static readonly ColorScheme StudioModernLight = new()
    {
        Primary = "#004b79",
        OnPrimary = "#ffffff",
        PrimaryContainer = "#0e639c",
        OnPrimaryContainer = "#beddff",
        Secondary = "#006b5b",
        OnSecondary = "#ffffff",
        SecondaryContainer = "#7df4d9",
        OnSecondaryContainer = "#00705f",
        Tertiary = "#693b27",
        OnTertiary = "#ffffff",
        TertiaryContainer = "#85523d",
        OnTertiaryContainer = "#ffcfbc",
        Error = "#ba1a1a",
        OnError = "#ffffff",
        ErrorContainer = "#ffdad6",
        OnErrorContainer = "#93000a",
        // Success and Warning lend the whole secondary and tertiary quadruples, Info the primary one.
        Success = "#006b5b",
        OnSuccess = "#ffffff",
        SuccessContainer = "#7df4d9",
        OnSuccessContainer = "#00705f",
        Warning = "#693b27",
        OnWarning = "#ffffff",
        WarningContainer = "#85523d",
        OnWarningContainer = "#ffcfbc",
        Info = "#004b79",
        OnInfo = "#ffffff",
        InfoContainer = "#0e639c",
        OnInfoContainer = "#beddff",
        Surface = "#fcf9f8",
        OnSurface = "#1b1b1c",
        SurfaceVariant = "#e5e2e1",
        OnSurfaceVariant = "#414750",
        // The design's outline grey #717881 reads 4.26 on this surface, under the 4.5 a text tone owes;
        // the third tone is that grey darkened just far enough, and stays fainter than OnSurfaceVariant.
        OnSurfaceVariant2 = "#6b7280",
        SurfaceContainerLowest = "#ffffff",
        SurfaceContainer = "#f0eded",
        SurfaceContainerLow = "#f6f3f2",
        SurfaceContainerHigh = "#eae7e7",
        SurfaceContainerHighest = "#e5e2e1",
        Background = "#fcf9f8",
        OnBackground = "#1b1b1c",
        Outline = "#717881",
        OutlineVariant = "#c1c7d1",
        InverseSurface = "#303030",
        InverseOnSurface = "#f3f0ef",
        InversePrimary = "#99cbff",
        Scrim = "#000000",
        Shadow = "#000000",
        ShadowUmbra = "rgba(0, 0, 0, 0.3)",
        ShadowPenumbra = "rgba(0, 0, 0, 0.15)",
    };

    /// <summary>The same workstation read at night, as the design draws it.</summary>
    private static readonly ColorScheme StudioModernDark = new()
    {
        Primary = "#99cbff",
        OnPrimary = "#003355",
        PrimaryContainer = "#0e639c",
        OnPrimaryContainer = "#beddff",
        Secondary = "#61dac1",
        OnSecondary = "#00382e",
        SecondaryContainer = "#13a38b",
        OnSecondaryContainer = "#003028",
        Tertiary = "#fab79d",
        OnTertiary = "#4e2513",
        TertiaryContainer = "#85523d",
        OnTertiaryContainer = "#ffcfbc",
        Error = "#ffb4ab",
        OnError = "#690005",
        ErrorContainer = "#93000a",
        OnErrorContainer = "#ffdad6",
        Success = "#61dac1",
        OnSuccess = "#00382e",
        SuccessContainer = "#13a38b",
        OnSuccessContainer = "#003028",
        Warning = "#fab79d",
        OnWarning = "#4e2513",
        WarningContainer = "#85523d",
        OnWarningContainer = "#ffcfbc",
        Info = "#99cbff",
        OnInfo = "#003355",
        InfoContainer = "#0e639c",
        OnInfoContainer = "#beddff",
        Surface = "#131313",
        OnSurface = "#e5e2e1",
        SurfaceVariant = "#353535",
        OnSurfaceVariant = "#c1c7d1",
        // This scheme's third tone is the design's own outline grey: above 4.5, below OnSurfaceVariant.
        OnSurfaceVariant2 = "#8b919b",
        SurfaceContainerLowest = "#0e0e0e",
        SurfaceContainer = "#202020",
        SurfaceContainerLow = "#1b1b1c",
        SurfaceContainerHigh = "#2a2a2a",
        SurfaceContainerHighest = "#353535",
        Background = "#131313",
        OnBackground = "#e5e2e1",
        Outline = "#8b919b",
        OutlineVariant = "#414750",
        InverseSurface = "#e5e2e1",
        InverseOnSurface = "#303030",
        InversePrimary = "#0c629b",
        Scrim = "#000000",
        Shadow = "#000000",
        ShadowUmbra = "rgba(0, 0, 0, 0.6)",
        ShadowPenumbra = "rgba(0, 0, 0, 0.3)",
    };

    /// <summary>The editor-workstation sibling: the Studio Modern design's blue on neutral chrome.</summary>
    public static readonly Palette StudioModern = new()
    {
        Id = StudioModernId,
        Name = "Studio Modern",
        Source = SourceName,
        Light = StudioModernLight,
        Dark = StudioModernDark,
    };

    /// <summary>Every palette this theme ships.</summary>
    public static IReadOnlyList<Palette> All => [Kinetic, Parchment, Operator, StudioModern];
}
