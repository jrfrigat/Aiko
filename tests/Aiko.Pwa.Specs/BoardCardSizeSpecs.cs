using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// Guards that a board card states its size, and states nothing at all when it has none.
/// </summary>
/// <remarks>
/// The size multiplies a card's score, so two cards with the same criteria read as different priorities
/// unless the coefficient is visible. It sits in the card's top row beside the id, as a badge carrying the
/// tone of the step it names - the same tone the settings ladder paints that step with.
/// </remarks>
public sealed class BoardCardSizeSpecs
{
    [Fact]
    public void A_board_card_shows_its_size_in_the_top_row_as_a_badged_tone()
    {
        var text = BoardSection();

        // The size is a tag in the top row, beside the id, and its tone comes from the one shared rule rather
        // than from a second palette written into this screen ...
        Assert.Contains("@if (!string.IsNullOrWhiteSpace(card.Size))", text, StringComparison.Ordinal);
        Assert.Contains(
            "<span class=\"aiko-tag @CardAppearance.SizeBadgeClass(card.Size, SizeGrid)\">@card.Size</span>",
            text,
            StringComparison.Ordinal);

        var topRow = text[..text.IndexOf("aiko-card__meta", StringComparison.Ordinal)];
        Assert.Contains("card.Size", topRow, StringComparison.Ordinal);

        // ... and the footer no longer states it: that row reads DoD on the left and the agents with the
        // stage's own state on the right.
        var footer = text[text.IndexOf("aiko-card__dod", StringComparison.Ordinal)..];
        Assert.Contains("@Agents(stage)", footer, StringComparison.Ordinal);
        Assert.DoesNotContain("@card.Size", footer, StringComparison.Ordinal);
    }

    [Fact]
    public void A_size_with_no_step_in_the_grid_keeps_the_quiet_badge()
    {
        var root = FindRepositoryRoot();
        var appearance = File.ReadAllText(
            Path.Combine(root, "src", "Aiko.Pwa", "Services", "CardAppearance.cs"));

        // A tone is a statement about a coefficient: a card that names a size the grid does not hold gets the
        // neutral badge, not another step's colour.
        Assert.Contains(
            "return step is null ? \"aiko-tag--quiet\" : SizeTagClass(step.Coefficient);",
            appearance,
            StringComparison.Ordinal);

        // ... and that resolution lives only there: a screen keeping its own copy is how two screens start to
        // disagree.
        Assert.DoesNotContain("step is null", BoardSection(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_size_tone_has_one_rule_for_the_settings_ladder_and_the_board()
    {
        var root = FindRepositoryRoot();

        // The direction is derived once, in the cockpit's appearance helper ...
        var appearance = File.ReadAllText(
            Path.Combine(root, "src", "Aiko.Pwa", "Services", "CardAppearance.cs"));
        Assert.Contains("public static string SizeTone(decimal coefficient)", appearance, StringComparison.Ordinal);
        Assert.Contains("public static string SizeTagClass(decimal coefficient)", appearance, StringComparison.Ordinal);
        Assert.Contains(
            "public static string SizeBadgeClass(string? size, IReadOnlyList<SizeDefinition> grid)",
            appearance,
            StringComparison.Ordinal);

        // ... the settings ladder asks that rule instead of repeating the comparison ...
        var settings = File.ReadAllText(
            Path.Combine(root, "src", "Aiko.Pwa", "Pages", "SettingsEditor.razor"));
        Assert.Contains("CardAppearance.SizeTone(coefficient)", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("> 1m => \"aiko-size--up\"", settings, StringComparison.Ordinal);

        // ... and the colour is declared once, on a variable both the tile and the badge read.
        var css = File.ReadAllText(
            Path.Combine(root, "src", "Aiko.Pwa", "wwwroot", "css", "app.css"));
        foreach (var tone in new[] { "up", "mid", "down" })
        {
            Assert.Contains($".aiko-tag--size-{tone}", css, StringComparison.Ordinal);
        }

        Assert.Contains("--aiko-tone:", css, StringComparison.Ordinal);
    }

    [Fact]
    public void A_card_without_a_size_prints_no_dash_and_no_zero()
    {
        var text = BoardSection();

        // The guard has no fallback expression: an unsized card prints nothing rather than an invented value.
        Assert.DoesNotContain("card.Size ??", text, StringComparison.Ordinal);
        Assert.DoesNotContain("card.Size ?", text, StringComparison.Ordinal);
        Assert.DoesNotContain("card.Size!", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_card_footer_wraps_so_a_size_cannot_stretch_a_narrow_column()
    {
        // The footer is the shared mono strip with the metadata line; it must keep wrapping, or the size tag
        // would push the agents out of a narrow column instead of folding onto the next line.
        var css = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "Aiko.Pwa", "wwwroot", "css", "app.css"));
        var start = css.IndexOf(".aiko-card__meta,", StringComparison.Ordinal);
        Assert.True(start >= 0, "The card footer should keep the shared strip rule.");
        var block = css[start..css.IndexOf('}', start)];
        Assert.Contains(".aiko-card__dod", block, StringComparison.Ordinal);
        Assert.Contains("flex-wrap: wrap;", block, StringComparison.Ordinal);
    }

    [Fact]
    public void The_card_page_banner_wears_the_same_size_tone()
    {
        var root = FindRepositoryRoot();
        var cardPage = File.ReadAllText(
            Path.Combine(root, "src", "Aiko.Pwa", "Pages", "CardPage.razor"));

        // The banner asks the one rule, with the project's grid, instead of printing a bare tag ...
        Assert.Contains(
            "CardAppearance.SizeBadgeClass(bannerSize, SizeGrid)",
            cardPage,
            StringComparison.Ordinal);
        Assert.Contains(
            "private IReadOnlyList<SizeDefinition> SizeGrid",
            cardPage,
            StringComparison.Ordinal);

        // ... so the badge the reader sees on the card page is the same colour as the one on the board.
        Assert.DoesNotContain("<span class=\"aiko-tag\">@bannerSize</span>", cardPage, StringComparison.Ordinal);
    }

    private static string BoardSection() => File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "src", "Aiko.Pwa", "Pages", "BoardSection.razor"));

    /// <summary>Walks up from this assembly to the solution file, as the other specs do.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(BoardCardSizeSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
