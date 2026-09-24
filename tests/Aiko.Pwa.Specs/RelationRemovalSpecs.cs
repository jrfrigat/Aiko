using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// The card's links can be taken back from the card itself: each one carries a button that asks, removes the link
/// through the daemon and reads the board again. A mistaken "blocks" used to be removable only by editing a file.
/// </summary>
/// <remarks>
/// Read from the markup, as the other screen specs do, until the PWA has component tests (TASK-216).
/// </remarks>
public sealed class RelationRemovalSpecs
{
    [Fact]
    public void Every_link_on_the_card_can_be_removed_after_asking()
    {
        var inspector = Read("src", "Aiko.Pwa", "Pages", "CardInspector.razor");
        var unlink = inspector[inspector.IndexOf("private async Task UnlinkAsync(CardRelation relation)", StringComparison.Ordinal)..];
        unlink = unlink[..unlink.IndexOf("\n    }", StringComparison.Ordinal)];

        Assert.Contains("OnClick=\"@(() => UnlinkAsync(link))\"", inspector, StringComparison.Ordinal);
        Assert.Contains("\"confirm\"", unlink, StringComparison.Ordinal);
        Assert.Contains("Http.DeleteAsync(", unlink, StringComparison.Ordinal);
        Assert.Contains("/relations/", unlink, StringComparison.Ordinal);
        Assert.Contains("State.LoadBoardAsync()", unlink, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Loc.resx")]
    [InlineData("Loc.ru.resx")]
    public void The_remove_control_speaks_both_languages(string resource)
    {
        var strings = Read("src", "Aiko.Pwa", "Resources", resource);

        Assert.Contains("name=\"RemoveLink\"", strings, StringComparison.Ordinal);
        Assert.Contains("name=\"UnlinkConfirm\"", strings, StringComparison.Ordinal);
        Assert.Contains("name=\"UnlinkFailed\"", strings, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. segments])).Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>Walks up from this assembly to the solution file, as the other specs do.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(RelationRemovalSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}
