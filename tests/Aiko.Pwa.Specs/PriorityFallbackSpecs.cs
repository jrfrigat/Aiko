using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// Every screen that shows a card's priority before the board has computed it falls back to the same figure
/// the calculator would give: the manual priority read on the 0..100 scale. A raw fallback shows a typed 25 as
/// "2500" and disagrees with the panel beside it.
/// </summary>
/// <remarks>
/// Read from the markup, as the other screen specs do, until the PWA has component tests (TASK-216).
/// </remarks>
public sealed class PriorityFallbackSpecs
{
    [Theory]
    [InlineData("BoardSection.razor")]
    [InlineData("CardInspector.razor")]
    [InlineData("CardPage.razor")]
    public void A_card_without_a_computed_priority_shows_its_manual_priority_on_the_board_scale(string page)
    {
        var markup = Read("src", "Aiko.Pwa", "Pages", page);

        Assert.Contains("PriorityCalculator.ManualScore(", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("EffectivePriority ?? card.OwnPriority", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("effective ?? Card.OwnPriority", markup, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. segments])).Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>Walks up from this assembly to the solution file, as the other specs do.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(PriorityFallbackSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}
