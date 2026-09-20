using Flare.Abstractions;
using Flare.Abstractions.Tokens;
using Flare.Abstractions.Tokens.Components;
using Flare.Theme.MaterialDesign3Expressive;
using Flare.Theming;

namespace Aiko.Theme.StitchFlow;

/// <summary>
/// The Aiko cockpit theme: the StitchFlow design language ("Kinetic Orchestration") expressed as
/// Flare tokens.
/// </summary>
/// <remarks>
/// The design is a command-grade dark workspace, and it disagrees with Material on the three axes a
/// theme exists to answer:
/// <list type="bullet">
/// <item><description><b>Shape</b> - a 2/4/8/12px ladder with pills reserved for status dots, where
/// Material rounds generously and uses capsules for selection.</description></item>
/// <item><description><b>Depth</b> - hairline borders and planar layering instead of tonal
/// elevation; shadows only lift overlays off the page.</description></item>
/// <item><description><b>Type</b> - Inter at a compact 11-32px ramp with JetBrains Mono for every
/// identifier, where Material sets Roboto much larger.</description></item>
/// </list>
/// Everything here starts from the Material 3 Expressive baseline and restates the groups the design
/// actually speaks. The groups it does not (charts, data grids, date pickers, ...) keep the
/// baseline's numbers: Aiko renders none of those components, and restating ~30 token records by hand
/// would add a hundred values nobody can see. The theme is its own style family with its own
/// <see cref="ITheme.StyleAssets"/>, so no Material stylesheet is loaded.
/// </remarks>
public static class StitchFlowTheme
{
    /// <summary>Stable theme id; also the CSS class the theme's stylesheets are scoped to.</summary>
    public const string ThemeId = "stitchflow";

    /// <summary>UI typeface. Identifiers use <see cref="MonoFontStack"/>.</summary>
    public const string UiFont = "Inter";

    /// <summary>
    /// Typeface stack for identifiers, triage lines, counts and telemetry; the value of
    /// <see cref="TypographyTokens.MonoFont"/>.
    /// </summary>
    /// <remarks>
    /// The mono face was core's own until Flare 0.38 took it as a token, and before that this stack lived
    /// in the theme stylesheet as an override of <c>.flare-text--mono</c>. A real family comes first and the
    /// generic is repeated last on purpose: a generic standing alone makes several engines use their own
    /// "monospace default size", which paints text at a size nothing else measured.
    /// </remarks>
    public const string MonoFontStack =
        "'JetBrains Mono', ui-monospace, SFMono-Regular, Menlo, Consolas, monospace";

    /// <summary>Builds the theme. Register it once, then select it by <see cref="ThemeId"/>.</summary>
    public static ITheme Create() => new MaterialDesign3ExpressiveTheme().Derive(
        id: ThemeId,
        displayName: "StitchFlow Cockpit",
        design: Apply,
        palettes: StitchFlowPalettes.All,
        defaultPaletteId: StitchFlowPalettes.KineticId,
        // The design's own fonts, plus the rules tokens cannot express (see the stylesheet).
        styleAssets:
        [
            "https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&family=JetBrains+Mono:wght@400;500;600&display=swap",
            $"_content/Aiko.Theme.StitchFlow/css/{ThemeId}.css",
        ],
        // Replaces the family's stylesheets entirely: nothing from Material applies to this subtree.
        styleFamilyId: ThemeId);

    /// <summary>Focus is a ring everywhere - the cockpit is keyboard-driven.</summary>
    private const string FocusRing = "2px solid var(--flare-color-primary)";

    /// <summary>The hairline every plane is bound by, at the design's 30% outline-variant.</summary>
    private const string Hairline =
        "1px solid color-mix(in srgb, var(--flare-color-outline-variant) 60%, transparent)";

    private static DesignTokens Apply(DesignTokens d) => d with
    {
        FocusRing = FocusRing,

        Typography = new TypographyTokens
        {
            // Core said `monospace` outright for every mono run until Flare 0.38 made the face a theme's
            // (migration to 0.38, §1). State it, or the cockpit's identifiers fall back to the browser's.
            MonoFont = MonoFontStack,
            DisplayLarge = Type(UiFont, "600", "2rem", "2.5rem", "-0.025em"),
            DisplayMedium = Type(UiFont, "600", "1.75rem", "2.25rem", "-0.02em"),
            DisplaySmall = Type(UiFont, "600", "1.5rem", "2rem", "-0.02em"),
            HeadlineLarge = Type(UiFont, "600", "1.5rem", "2rem", "-0.02em"),
            // The board's column titles: 18px, tight, semibold.
            HeadlineMedium = Type(UiFont, "600", "1.125rem", "1.5rem", "-0.015em"),
            HeadlineSmall = Type(UiFont, "600", "1rem", "1.375rem", "-0.01em"),
            TitleLarge = Type(UiFont, "600", "1rem", "1.375rem", "-0.01em"),
            TitleMedium = Type(UiFont, "600", "0.9375rem", "1.25rem", "-0.005em"),
            TitleSmall = Type(UiFont, "600", "0.875rem", "1.25rem", "0"),
            BodyLarge = Type(UiFont, "400", "0.875rem", "1.375rem", "0"),
            // 13px/20px is the app's body size in the design.
            BodyMedium = Type(UiFont, "400", "0.8125rem", "1.25rem", "0"),
            BodySmall = Type(UiFont, "400", "0.75rem", "1rem", "0"),
            LabelLarge = Type(UiFont, "500", "0.8125rem", "1.125rem", "0.01em"),
            LabelMedium = Type(UiFont, "500", "0.75rem", "1rem", "0.02em"),
            // The caption/eyebrow scale: 11px, 500, wide tracking - "WORKSPACE FLOW", "DoD: ...".
            LabelSmall = Type(UiFont, "500", "0.6875rem", "0.875rem", "0.04em"),
        },

        // The radius ladder. Pills stay available (Full) for status dots and count badges only -
        // every other shape in this theme points at Small/ExtraSmall deliberately.
        Shape = d.Shape with
        {
            None = "0px",
            ExtraSmall = "2px",
            Small = "4px",
            Medium = "6px",
            Large = "8px",
            ExtraLarge = "12px",
            Full = "9999px",
        },

        // Hairlines, not blur: only overlays get a real shadow.
        Elevation = new ElevationTokens
        {
            Level0 = "none",
            Level1 = "0 1px 2px rgba(0, 0, 0, 0.35)",
            Level2 = "0 1px 12px rgba(0, 0, 0, 0.4)",
            Level3 = "inset 0 1px 0 0 rgba(255, 255, 255, 0.06), 0 4px 20px -2px rgba(0, 0, 0, 0.6)",
            Level4 = "inset 0 1px 0 0 rgba(255, 255, 255, 0.08), 0 10px 28px -4px rgba(0, 0, 0, 0.7)",
            Level5 = "inset 0 1px 0 0 rgba(255, 255, 255, 0.10), 0 16px 44px -6px rgba(0, 0, 0, 0.8)",
        },

        State = d.State with
        {
            HoverOpacity = "0.06",
            FocusOpacity = "0.12",
            PressedOpacity = "0.12",
            SelectedOpacity = "0.10",
            DraggedOpacity = "0.60",
            DisabledOpacity = "0.38",
            DisabledContainerOpacity = "0.12",
        },

        Border = d.Border with
        {
            Width = "1px",
            WidthEmphasis = "2px",
        },

        // The design hides scrollbars; Aiko keeps them thin instead - a board that scrolls sideways
        // and an inspector that scrolls vertically both need the affordance.
        Scrollbar = d.Scrollbar with
        {
            Width = "thin",
            Size = "6px",
            Thumb = "var(--flare-color-outline-variant)",
            ThumbHover = "var(--flare-color-outline)",
            Track = "transparent",
            Radius = "var(--flare-shape-full)",
        },

        // ---------------------------------------------------------------- Button ---------------
        // A compact 24-40px ramp with a 4px corner. Selection does NOT reshape: the design states
        // selection as a fill/accent swap, so every SelectedRadius points back at the rest radius.
        Button = d.Button with
        {
            ContainerRadius = "var(--flare-shape-small)",
            RadiusXs = CornerRadiusTokens.All("var(--flare-shape-small)"),
            RadiusSm = CornerRadiusTokens.All("var(--flare-shape-small)"),
            RadiusMd = CornerRadiusTokens.All("var(--flare-shape-small)"),
            RadiusLg = CornerRadiusTokens.All("var(--flare-shape-small)"),
            RadiusXl = CornerRadiusTokens.All("var(--flare-shape-small)"),

            HeightXs = "1.5rem",
            HeightSm = "1.75rem",
            HeightMd = "2rem",
            HeightLg = "2.25rem",
            HeightXl = "2.5rem",

            PaddingInlineXs = "0.5rem",
            PaddingInlineSm = "0.625rem",
            PaddingInlineMd = "0.75rem",
            PaddingInlineLg = "0.875rem",
            PaddingInlineXl = "1rem",

            TextPaddingInlineXs = "0.375rem",
            TextPaddingInlineSm = "0.5rem",
            TextPaddingInlineMd = "0.625rem",
            TextPaddingInlineLg = "0.75rem",
            TextPaddingInlineXl = "0.875rem",

            GapXs = "0.25rem",
            GapSm = "0.375rem",
            GapMd = "0.375rem",
            GapLg = "0.5rem",
            GapXl = "0.5rem",

            OutlineWidthXs = "1px",
            OutlineWidthSm = "1px",
            OutlineWidthMd = "1px",
            OutlineWidthLg = "1px",
            OutlineWidthXl = "1px",

            IconSizeXs = "0.875rem",
            IconSizeSm = "0.875rem",
            IconSizeMd = "1rem",
            IconSizeLg = "1.125rem",
            IconSizeXl = "1.25rem",

            LabelXs = Type(UiFont, "500", "0.75rem", "1rem", "0"),
            LabelSm = Type(UiFont, "500", "0.75rem", "1rem", "0"),
            LabelMd = Type(UiFont, "500", "0.8125rem", "1.125rem", "0"),
            LabelLg = Type(UiFont, "500", "0.875rem", "1.25rem", "0"),
            LabelXl = Type(UiFont, "500", "0.9375rem", "1.25rem", "0"),

            SelectedRadiusXs = "var(--flare-shape-small)",
            SelectedRadiusSm = "var(--flare-shape-small)",
            SelectedRadiusMd = "var(--flare-shape-small)",
            SelectedRadiusLg = "var(--flare-shape-small)",
            SelectedRadiusXl = "var(--flare-shape-small)",
            SelectedRadiusSquare = "var(--flare-shape-small)",

            // Selected states. A segmented control paints its active segment with the GENERIC pair -
            // togglebutton.css restates --flare-btn-selected-bg/color for `> .flare-btn--selected`
            // rather than the variant's pair, so this is where the accent fill has to be for the
            // design's active segment (and it is the same answer for a selected standalone button).
            SelectedBg = "var(--flare-color-primary)",
            SelectedColor = "var(--flare-color-on-primary)",
            // The per-variant pairs cover a toggle in any OTHER container, where the variant's own
            // rule wins.
            FilledSelectedBg = "var(--flare-color-primary)",
            FilledSelectedColor = "var(--flare-color-on-primary)",
            ElevatedSelectedBg = "var(--flare-color-surface-container-high)",
            ElevatedSelectedColor = "var(--flare-color-primary)",
            TonalSelectedBg = "var(--flare-color-primary-container)",
            TonalSelectedColor = "var(--flare-color-on-primary-container)",
            OutlinedSelectedBg = "var(--flare-color-surface-container-highest)",
            OutlinedSelectedColor = "var(--flare-color-primary)",

            // An unselected toggle is a neutral container, never the accent fill.
            FilledUnselectedBg = "var(--flare-color-surface-container)",
            FilledUnselectedColor = "var(--flare-color-on-surface-variant)",

            FocusOutline = FocusRing,
            FocusOutlineOffset = "1px",
            FocusShadow = "0 0 0 3px color-mix(in srgb, var(--flare-color-primary) 25%, transparent)",
            // The design's "primary button glows" rule.
            FilledHoverShadow = "0 0 14px color-mix(in srgb, var(--flare-color-primary) 30%, transparent)",
            DisabledOpacity = "0.38",
            DisabledLayer = "var(--flare-color-on-surface)",
            LoadingOpacity = "0.60",
        },

        // ------------------------------------------------------------------ Card ---------------
        // Planes bound by hairline borders. Fill (the board's column/card plane) and Outlined (the
        // design's panel) are the two Aiko uses; elevation lifts an overlay, never a resting card.
        Card = d.Card with
        {
            Radius = "var(--flare-shape-large)",
            FilledBg = "var(--flare-color-surface-container-low)",
            FilledBorder = Hairline,
            FilledElevation = "var(--flare-elevation-1)",
            OutlinedBg = "transparent",
            OutlinedBorder = Hairline,
            OutlinedElevation = "none",
            ElevatedBg = "var(--flare-color-surface-container-low)",
            Elevation = "var(--flare-elevation-2)",
            ElevationHover = "var(--flare-elevation-3)",
            TonalBg = "var(--flare-color-surface-container)",
            TonalColor = "var(--flare-color-on-surface)",
            TonalElevation = "none",
            TextColor = "var(--flare-color-on-surface)",
            TextElevation = "none",
            SelectedBg = "var(--flare-color-surface-container)",
            SelectedBorder = "1px solid var(--flare-color-primary)",
            StateLayer = "var(--flare-state-hover-layer)",

            PaddingTop = "0.75rem",
            PaddingRight = "0.75rem",
            PaddingBottom = "0.75rem",
            PaddingLeft = "0.75rem",
            ContentPadding = "0.75rem",
            HeaderPadding = "0 0 0.5rem",
            FooterPadding = "0.5rem 0 0",
            ActionsPadding = "0.5rem 0 0",
            ActionsGap = "0.375rem",
            MediaRadius = "var(--flare-shape-small)",

            TitleColor = "var(--flare-color-on-surface)",
            TitleFontFamily = UiFont,
            TitleFontSize = "var(--flare-typescale-body-medium-size)",
            SubtitleColor = "var(--flare-color-on-surface-variant)",
            SubtitleFontFamily = UiFont,
            SubtitleFontSize = "var(--flare-typescale-label-small-size)",

            TransitionDuration = "var(--flare-motion-duration-short2)",
            TransitionEasing = "var(--flare-motion-easing-standard)",
        },

        // ------------------------------------------------------------------ Chip ---------------
        // Micro-chips: the design's 2px "rounded", used for story tags and policy tags.
        Chip = d.Chip with
        {
            Radius = "var(--flare-shape-extra-small)",
            Height = "1.25rem",
            FilledBg = "var(--flare-color-surface-container)",
            ElevatedBg = "var(--flare-color-surface-container-high)",
            // The chip is the design's monospaced micro-tag: JetBrains Mono at 11px, medium, tight. Core
            // declared none of the label's type, so the theme repainted `.flare-chip` in its stylesheet
            // until Flare 0.38 added these tokens (migration to 0.38, §1). All five steps carry the same
            // size, which is what the stylesheet said: a micro-tag does not have a size ramp.
            LabelFont = MonoFontStack,
            LabelWeight = "500",
            LabelSpacing = "0.02em",
            LabelSizeXs = "0.6875rem",
            LabelSizeSm = "0.6875rem",
            LabelSizeMd = "0.6875rem",
            LabelSizeLg = "0.6875rem",
            LabelSizeXl = "0.6875rem",
            IconSizeXs = "0.75rem",
            IconSizeSm = "0.8125rem",
            IconSizeMd = "0.875rem",
            IconSizeLg = "1rem",
            IconSizeXl = "1.125rem",
            AvatarSizeXs = "1rem",
            AvatarSizeSm = "1.125rem",
            AvatarSizeMd = "1.25rem",
            AvatarSizeLg = "1.5rem",
            AvatarSizeXl = "1.75rem",
            PaddingInlineXs = "0.25rem",
            PaddingInlineSm = "0.375rem",
            PaddingInlineMd = "0.375rem",
            PaddingInlineLg = "0.5rem",
            PaddingInlineXl = "0.625rem",
        },

        // ----------------------------------------------------------------- Input ---------------
        // 28-40px fields with the same 4px corner as a button, so a filter row lines up.
        Input = d.Input with
        {
            OutlinedRadius = "var(--flare-shape-small)",
            BorderColor = "var(--flare-color-outline-variant)",
            BorderBottomColor = "var(--flare-color-outline)",
            HoverBorderBottomColor = "var(--flare-color-outline)",
            FilledBg = "var(--flare-color-surface-container)",
            FocusRing = "0 0 0 2px color-mix(in srgb, var(--flare-color-primary) 45%, transparent)",
            FocusOutline = FocusRing,
            FocusOutlineOffset = "1px",
            HoverStateLayer = "var(--flare-state-hover-layer)",
            HeightXs = "1.75rem",
            HeightSm = "1.75rem",
            HeightMd = "2rem",
            HeightLg = "2.25rem",
            HeightXl = "2.5rem",
            PaddingXs = "0 0.5rem",
            PaddingSm = "0 0.5rem",
            PaddingMd = "0 0.625rem",
            PaddingLg = "0 0.75rem",
            PaddingXl = "0 0.875rem",
            IconSize = "1rem",
            PlaceholderColor = "var(--flare-color-outline)",
            DisabledBg = "var(--flare-color-surface-container)",
            DisabledIndicator = "var(--flare-color-outline-variant)",
            ErrorHoverIndicator = "var(--flare-color-on-error-container)",
            DisabledOpacity = "0.38",
        },

        // ------------------------------------------------------------------ List ---------------
        // The inspector's fact rows: dense, 11-12px, trailing value in the muted tone.
        List = d.List with
        {
            Bg = "transparent",
            Radius = "var(--flare-shape-small)",
            Divider = Hairline,
            ItemHeight = "1.5rem",
            ItemHeightTwoLine = "2.25rem",
            ItemHeightDense = "1.25rem",
            ItemHeightTwoLineDense = "2rem",
            ItemPaddingBlock = "0.125rem",
            ItemPaddingBlockDense = "0.125rem",
            ItemPaddingInline = "0.375rem",
            ItemGap = "0.25rem",
            ItemContentGap = "0.5rem",
            ItemRadius = "var(--flare-shape-small)",
            ItemLabelFont = UiFont,
            ItemLabelSize = "var(--flare-typescale-label-small-size)",
            ItemColor = "var(--flare-color-on-surface)",
            ItemTrailingColor = "var(--flare-color-on-surface-variant)",
            ItemSelectedBg = "var(--flare-color-surface-container-high)",
            ItemSelectedColor = "var(--flare-color-on-surface)",
            ItemDisabledOpacity = "0.38",
            ItemIconSize = "1rem",
        },

        // ------------------------------------------------------------------ Nav ---------------
        // The rail: 32px rows, 4px corners, and an active item that is a filled row carrying the
        // accent's left bar - the v2 frame states "you are here" with the bar, not with a glow.
        Nav = d.Nav with
        {
            ItemHeight = "2rem",
            ItemRadius = "var(--flare-shape-small)",
            IndicatorRadius = "var(--flare-shape-small)",
            ActiveIndicator = "var(--flare-color-surface-container-high)",
            ActiveLeftBar = "2px solid var(--flare-color-primary)",
            // "You are here" is the left bar plus the accent label, and the label is read against the
            // filled row above rather than against the drawer plane. Stated rather than left to the base
            // theme: until Flare 0.38 core fixed this to the on-secondary-container pair of an indicator
            // this theme does not use, and the theme repainted the rule in its stylesheet to get the accent
            // (migration to 0.38, §1). The other four colours stay inherited - the in-box values are what
            // core held, so stating them would move nothing.
            ActiveColor = "var(--flare-color-primary)",
            ActiveWeight = "600",
            BadgeWeight = "500",
            IconSize = "1.125rem",
            LinkDisabledOpacity = "0.38",
            RailLabelLineHeight = "1.125rem",
        },

        // ----------------------------------------------------------------- Layout ------------
        // A 56px app bar over the darkest plane, a 256px rail, and 12px content gutters. The chrome sits
        // on the design's darkest plane and the canvas on `surface`, and the rail stands off the canvas by
        // depth rather than by a rule. All of it belongs to the theme since Flare 0.38 gave the shell's
        // three planes and the rail's shadow tokens (migration to 0.38, §1); before that these were three
        // class-name rules in the theme stylesheet, because core picked the drawer's and the content's
        // roles itself.
        Layout = d.Layout with
        {
            AppBarHeight = "3.5rem",
            AppBarHeightDense = "3rem",
            AppBarBg = "var(--flare-layout-shell-bg)",
            AppBarBorder = "none",
            AppBarShadow = "0 1px 8px rgba(0, 0, 0, 0.4)",
            ContentPadding = "0.75rem",
            ContentPaddingMobile = "0.5rem",
            DrawerWidth = "16rem",
            DrawerRailWidth = "3.5rem",
            DrawerBorder = "none",
            // The darkest plane of the palette is the chrome; the canvas is `surface`; the rail shares the
            // drawer's plane so the two read as one surface.
            ShellBg = "var(--flare-color-surface-container-lowest)",
            DrawerBg = "var(--flare-color-surface-container-lowest)",
            RailBg = "var(--flare-layout-drawer-bg)",
            ContentBg = "var(--flare-color-surface)",
            // The rail's shadow: core owns the sign (it lands on the edge facing the content for either
            // anchor and under either writing direction), so this is the magnitude. `0`, the in-box value,
            // would draw none.
            DrawerShadowOffset = "1px",
            DrawerShadowBlur = "10px",
            DrawerShadowColor = "rgba(0, 0, 0, 0.3)",
        },

        // ----------------------------------------------------------------- Drawer ------------
        Drawer = d.Drawer with
        {
            Width = "26rem",
            MiniWidth = "3.5rem",
            Border = "1px solid color-mix(in srgb, var(--flare-color-outline-variant) 30%, transparent)",
            SectionBorder = Hairline,
        },

        // --------------------------------------------------------- Segmented controls ---------
        // The design's mode switch: a hairline container holding an accent-filled active segment.
        ToggleButton = d.ToggleButton with
        {
            GroupBorder = Hairline,
            GroupRadius = "var(--flare-shape-small)",
            GroupRadiusVertical = "var(--flare-shape-small)",
            GroupDivider = "none",
        },
        ButtonGroup = d.ButtonGroup with { ConnectedGap = "0", StandardGapMd = "0.25rem" },

        // ---------------------------------------------------------------- Progress -----------
        // The telemetry bar: a 4px rule at Xs with a full-round track.
        Progress = d.Progress with
        {
            LinearHeightXs = "0.25rem",
            LinearHeightSm = "0.375rem",
            LinearHeightMd = "0.5rem",
            LinearHeightLg = "0.625rem",
            LinearHeightXl = "0.75rem",
            TrackRadius = "var(--flare-shape-full)",
            CircularSizeXs = "1rem",
            CircularSizeSm = "1.25rem",
            CircularSizeMd = "1.75rem",
            CircularSizeLg = "2.5rem",
            CircularSizeXl = "3.5rem",
            CircularWidthXs = "2px",
            CircularWidthSm = "2px",
            CircularWidthMd = "3px",
            CircularWidthLg = "4px",
            CircularWidthXl = "5px",
            CircularCap = "round",
        },

        // ----------------------------------------------------------------- Tabs --------------
        // The artifact switcher: an underline bar, not a filled pill.
        Tabs = d.Tabs with
        {
            ActiveWeight = "600",
            LabelFont = UiFont,
            LabelSize = "var(--flare-typescale-label-small-size)",
            LabelWeight = "500",
            LabelSpacing = "0.02em",
            IndicatorThickness = "2px",
            SecondaryIndicatorThickness = "2px",
            ActiveColor = "var(--flare-color-primary)",
            SecondaryActiveColor = "var(--flare-color-primary)",
            InactiveColor = "var(--flare-color-on-surface-variant)",
            DividerColor = "color-mix(in srgb, var(--flare-color-outline-variant) 60%, transparent)",
            SelectedBg = "var(--flare-color-surface-container-high)",
            SelectedFg = "var(--flare-color-primary)",
            FilledBg = "var(--flare-color-surface-container)",
            FilledFg = "var(--flare-color-on-surface-variant)",
            TrackBg = "var(--flare-color-surface-container-low)",
            PillRadius = "var(--flare-shape-small)",
            TabHeight = "2.25rem",
            TabMinWidth = "3rem",
            TabPaddingInline = "0.625rem",
            IconSize = "1rem",
            TabDisabledOpacity = "0.38",
        },

        // --------------------------------------------------------------- Timeline ------------
        // The execution log: 8px dots on a 1px rail.
        Timeline = d.Timeline with
        {
            DotSize = "0.5rem",
            DotBg = "var(--flare-color-surface-container-highest)",
            DotBorderWidth = "1px",
            DotIconSize = "0.75rem",
            LineWidth = "1px",
            LineColor = "color-mix(in srgb, var(--flare-color-outline-variant) 60%, transparent)",
            ConnectorWidth = "1px",
        },

        // ------------------------------------------------------------------ Menu -------------
        // Dropdown panels (a select's popup): the same plane language as a card.
        Menu = d.Menu with
        {
            EnterAnimation = "none",
            PanelMinWidth = "10rem",
            PanelRadius = "var(--flare-shape-large)",
            PanelBg = "var(--flare-color-surface-container-low)",
            PanelShadow = "var(--flare-elevation-3)",
            PanelPaddingBlock = "0.25rem",
            PanelPaddingInline = "0.25rem",
            ItemHeight = "1.75rem",
            ItemPaddingBlock = "0.125rem",
            ItemPaddingInline = "0.5rem",
            ItemPaddingBlockDense = "0.125rem",
            ItemGap = "0.25rem",
            ItemGapDense = "0.25rem",
            ItemGapBetween = "0.5rem",
            ItemIconSize = "1rem",
            ItemRadius = "var(--flare-shape-small)",
            ItemRadiusEnd = "var(--flare-shape-small)",
            GroupRadius = "var(--flare-shape-small)",
            GroupPadding = "0 0.25rem 0.25rem",
            GroupBg = "transparent",
            GroupedPanelBg = "var(--flare-color-surface-container-low)",
            GroupedPanelShadow = "var(--flare-elevation-3)",
            ItemLabelFont = UiFont,
            ItemLabelWeight = "400",
            ItemLabelSize = "var(--flare-typescale-label-small-size)",
            ItemLabelHeight = "1rem",
            ItemLabelSpacing = "0.01em",
            ItemFocusRingColor = "var(--flare-color-primary)",
            ItemFocusRingThickness = "1px",
            ItemFocusRingOffset = "0px",
            ItemDisabledOpacity = "0.38",
        },

        // ----------------------------------------------------------------- Badge ------------
        // Count pills and status dots - the one place a capsule is allowed.
        Badge = d.Badge with
        {
            Radius = "var(--flare-shape-full)",
            MinWidthXs = "1rem",
            MinWidthSm = "1.125rem",
            MinWidthMd = "1.25rem",
            MinWidthLg = "1.5rem",
            MinWidthXl = "1.75rem",
            HeightXs = "1rem",
            HeightSm = "1.125rem",
            HeightMd = "1.25rem",
            HeightLg = "1.5rem",
            HeightXl = "1.75rem",
            DotSizeXs = "0.375rem",
            DotSizeSm = "0.5rem",
            DotSizeMd = "0.5rem",
            DotSizeLg = "0.625rem",
            DotSizeXl = "0.75rem",
            PaddingXXs = "0.125rem",
            PaddingXSm = "0.25rem",
            PaddingXMd = "0.25rem",
            PaddingXLg = "0.375rem",
            PaddingXXl = "0.5rem",
            LabelSizeXs = "0.5625rem",
            LabelSizeSm = "0.625rem",
            LabelSizeMd = "0.6875rem",
            LabelSizeLg = "0.75rem",
            LabelSizeXl = "0.8125rem",
            Offset = "0.125rem",
            DotOffset = "0.125rem",
        },

        // ------------------------------------------------------------------ Drag ------------
        // The zone lights up with the accent; the source dims.
        Drag = d.Drag with
        {
            SourceOpacity = "0.4",
            PreviewElevation = "var(--flare-elevation-3)",
            PreviewOpacity = "1",
            ZoneActiveBackground = "color-mix(in srgb, var(--flare-color-primary) 8%, transparent)",
            ZoneActiveOutline = "1px dashed var(--flare-color-primary)",
            IndicatorColor = "var(--flare-color-primary)",
        },

        // ---------------------------------------------------------- Overlays & chrome --------
        Tooltip = d.Tooltip with { MaxWidth = "20rem", Offset = "0.375rem" },
        Dialog = d.Dialog with { Radius = "var(--flare-shape-large)", IconSize = "1.25rem" },
        Popover = d.Popover with { Radius = "var(--flare-shape-large)" },
        Snackbar = d.Snackbar with
        {
            Radius = "var(--flare-shape-large)",
            MinHeight = "2.5rem",
            MinWidth = "16rem",
            MaxWidth = "26rem",
            PaddingBlock = "0.625rem",
        },
        Alert = d.Alert with
        {
            Radius = "var(--flare-shape-large)",
            BorderWidth = "1px",
            Padding = "0.75rem",
            Gap = "0.5rem",
        },
        AppBar = d.AppBar with
        {
            Gap = "0.75rem",
            Height = "3.5rem",
            HeightDense = "2.75rem",
            PaddingX = "0.75rem",
            TitlePaddingX = "0.5rem",
            Border = "none",
        },
        Scrim = d.Scrim with { Opacity = "0.7" },
    };


    /// <summary>One type-scale entry: family, weight, size, line height and tracking.</summary>
    private static TypeStyle Type(
        string family, string weight, string size, string lineHeight, string spacing) => new()
        {
            FontFamily = family,
            FontWeight = weight,
            FontSize = size,
            LineHeight = lineHeight,
            LetterSpacing = spacing,
        };
}
