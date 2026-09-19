using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// Guards that a board card states its size, and states nothing at all when it has none.
/// </summary>
/// <remarks>
/// The size multiplies a card's score, so two cards with the same criteria read as different priorities
/// unless the coefficient is visible. TASK-46 put it in the card's footer, beside the stage's agents.
/// </remarks>
public sealed class BoardCardSizeSpecs
{
    [Fact]
    public void A_board_card_shows_its_size_beside_the_stage_agents()
    {
        var text = BoardSection();

        // The size is a quiet tag in the footer, drawn in the right-hand group next to the agents ...
        Assert.Contains("@Agents(stage)", text, StringComparison.Ordinal);
        Assert.Contains("@if (!string.IsNullOrWhiteSpace(card.Size))", text, StringComparison.Ordinal);
        Assert.Contains("<span class=\"aiko-tag aiko-tag--quiet\">@card.Size</span>", text, StringComparison.Ordinal);

        // ... and it is the footer's right side, next to the agents, not a line of its own.
        var footer = text[text.IndexOf("aiko-card__dod", StringComparison.Ordinal)..];
        Assert.Contains("@Agents(stage)", footer, StringComparison.Ordinal);
        Assert.Contains("<span class=\"aiko-tag aiko-tag--quiet\">@card.Size</span>", footer, StringComparison.Ordinal);

        // It is not in the top row, where the type, the stage state and the priority already sit.
        var topRow = text[..text.IndexOf("aiko-card__meta", StringComparison.Ordinal)];
        Assert.DoesNotContain("card.Size", topRow, StringComparison.Ordinal);
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
