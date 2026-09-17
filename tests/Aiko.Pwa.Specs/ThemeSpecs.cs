using Aiko.Theme.StitchFlow;
using Flare.Abstractions;
using Flare.Abstractions.Tokens;
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
        // The chrome plane is the darkest one; the canvas is `surface`.
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

        // A compact control ramp: no button in this theme is taller than a 40px toolbar control.
        Assert.Equal("1.5rem", design.Button.HeightXs);
        Assert.Equal("2.5rem", design.Button.HeightXl);

        // Selection is a fill swap, never a reshape: the selected radius is the rest radius.
        Assert.Equal(design.Button.RadiusMd.TopLeft, design.Button.SelectedRadiusMd);
    }
}
