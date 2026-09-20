using Aiko.Theme.StitchFlow;
using Flare.Abstractions;
using Flare.Abstractions.Tokens;
using System.Text.RegularExpressions;
using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// Guards the theme package the PWA ships. The theme is the app's whole visual identity, and every
/// failure mode here is silent at build time: a derived theme that keeps the base's style family loads
/// Material's stylesheets over the cockpit's, a default palette id that is not in the palette list
/// falls back to an arbitrary one, and a token group that quietly keeps the base's numbers looks like
/// a bug in the component rather than in the theme.
/// </summary>
public sealed class ThemeSpecs
{
    private static readonly ITheme Theme = StitchFlowTheme.Create();

    [Fact]
    public void Theme_owns_its_style_family()
    {
        Assert.Equal(StitchFlowTheme.ThemeId, Theme.Id);
        // Its own family: none of the base theme's stylesheets can apply to this subtree.
        Assert.Equal(Theme.Id, Theme.StyleFamilyId);
        Assert.Contains(
            $"_content/Aiko.Theme.StitchFlow/css/{Theme.Id}.css",
            Theme.StyleAssets);
    }

    [Fact]
    public void Default_palette_exists_in_the_themes_own_palettes()
    {
        Assert.Contains(Theme.Palettes, palette => palette.Id == Theme.DefaultPaletteId);
        Assert.All(Theme.Palettes, palette => Assert.Equal(StitchFlowPalettes.SourceName, palette.Source));
    }

    [Fact]
    public void Dark_scheme_keeps_the_designs_own_colors()
    {
        var dark = StitchFlowPalettes.Kinetic.Dark;

        Assert.Equal("#c0c1ff", dark.Primary);
        Assert.Equal("#89ceff", dark.Secondary);
        Assert.Equal("#4edea3", dark.Tertiary);
        // The chrome plane is the darkest one; the canvas is `surface`. Flare 0.38 gave the design's sixth
        // plane a role of its own, so the chrome names SurfaceContainerLowest instead of borrowing
        // Background - the colour of the document - as a substitute for a panel.
        Assert.Equal("#060e20", dark.SurfaceContainerLowest);
        Assert.Equal("#060e20", dark.Background);
        Assert.Equal("#0b1326", dark.Surface);
        Assert.Equal("#131b2e", dark.SurfaceContainerLow);
    }

    [Fact]
    public void Cockpit_restates_shape_type_and_controls()
    {
        var design = Theme.Design;

        // The radius ladder, and the one place a capsule is allowed.
        Assert.Equal("2px", design.Shape.ExtraSmall);
        Assert.Equal("4px", design.Shape.Small);
        Assert.Equal("8px", design.Shape.Large);

        // 13px body text and Inter, not the baseline's Roboto at Material's sizes.
        Assert.Equal(StitchFlowTheme.UiFont, design.Typography.BodyMedium.FontFamily);
        Assert.Equal("0.8125rem", design.Typography.BodyMedium.FontSize);
        // The mono face is the theme's own since Flare 0.38, and every identifier is read in it.
        Assert.Equal(StitchFlowTheme.MonoFontStack, design.Typography.MonoFont);

        // The shell's three planes and the rail's shadow are the theme's since 0.38, instead of three
        // class-name rules in the stylesheet that repainted what core had decided.
        Assert.Equal("var(--flare-color-surface-container-lowest)", design.Layout.ShellBg);
        Assert.Equal("var(--flare-layout-drawer-bg)", design.Layout.RailBg);
        Assert.Equal("var(--flare-color-surface)", design.Layout.ContentBg);
        Assert.Equal("1px", design.Layout.DrawerShadowOffset);

        // An active nav label is read against its own filled row, so it takes the accent - not the
        // on-secondary-container pair of an indicator this theme does not use.
        Assert.Equal("var(--flare-color-primary)", design.Nav.ActiveColor);

        // A compact control ramp: no button in this theme is taller than a 40px toolbar control.
        Assert.Equal("1.5rem", design.Button.HeightXs);
        Assert.Equal("2.5rem", design.Button.HeightXl);

        // Selection is a fill swap, never a reshape: the selected radius is the rest radius.
        Assert.Equal(design.Button.RadiusMd.TopLeft, design.Button.SelectedRadiusMd);
    }

    [Fact]
    public void The_first_frames_mode_is_stated_once_and_agreed_in_three_places()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(
            Path.Combine(root, "src", "Aiko.Pwa", "wwwroot", "index.html"));
        var program = File.ReadAllText(Path.Combine(root, "src", "Aiko.Pwa", "Program.cs"));

        // The bootstrap script paints the first frame from the attribute, and the theme service applies
        // this default once .NET is up. The two once disagreed, and a fresh visitor got the dark frame
        // painted over by a light theme - the one flash neither side can undo.
        var attribute = Regex.Match(page, @"data-default-mode=""(?<mode>[a-z]+)""");
        var configured = Regex.Match(program, @"DefaultMode\s*=\s*ThemeMode\.(?<mode>[A-Za-z]+)");
        Assert.True(attribute.Success, "index.html should state the mode a first visit gets.");
        Assert.True(configured.Success, "Program.cs should state the mode it configures.");
        Assert.Equal(configured.Groups["mode"].Value, attribute.Groups["mode"].Value, ignoreCase: true);

        // And it is "auto": a workspace on someone's own machine should look like the rest of it until
        // the person says otherwise.
        Assert.Equal("Auto", configured.Groups["mode"].Value);

        // The third place is not a copy of the mode but the condition for it: the provider is the only
        // thing that reads prefers-color-scheme, so with this off "auto" resolves against nothing.
        var app = File.ReadAllText(Path.Combine(root, "src", "Aiko.Pwa", "App.razor"));
        Assert.DoesNotContain("RespectSystemColorScheme=\"false\"", app, StringComparison.Ordinal);
    }

    [Fact]
    public void The_splash_paints_the_first_frames_own_colour()
    {
        var css = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "Aiko.Pwa", "wwwroot", "css", "app.css"));

        // The splash paints before the theme's variables exist, so its fallback is the colour of the
        // first frame - and which frame that is, the bootstrap script has already said by putting
        // `flare-mode-dark` on <html>. A single dark fallback painted a dark splash under light mode.
        Assert.Contains("var(--flare-color-background, #f7f8fc)", css, StringComparison.Ordinal);
        Assert.Contains("html.flare-mode-dark #flare-splash", css, StringComparison.Ordinal);
        Assert.Contains("var(--flare-color-background, #060e20)", css, StringComparison.Ordinal);

        // The mark is an accent bar, and the dark palette's pale periwinkle disappears on a light plane.
        Assert.Contains("var(--flare-color-primary, #4a4bc4)", css, StringComparison.Ordinal);
        Assert.Contains("html.flare-mode-dark .splash-mark i", css, StringComparison.Ordinal);
    }

    /// <summary>
    /// Walks up from this assembly to the solution file, the same way the markup specs do.
    /// </summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(ThemeSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
