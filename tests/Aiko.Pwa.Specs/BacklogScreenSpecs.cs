using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// Guards the one road the backlog screen takes into a card.
/// </summary>
/// <remarks>
/// The defect these pin: the card's number was a dead tag, and the card opened only from a separate Open
/// button whose address was built from the route parameter <c>ProjectId</c> - a value that can still
/// arrive as the project's immutable id - instead of the readable handle every other page speaks.
/// </remarks>
public sealed class BacklogScreenSpecs
{
    [Fact]
    public void The_card_number_is_the_link_into_the_card()
    {
        var page = RazorPage("BacklogPage.razor");

        // The number is not drawn as text: it is the link into the card, and it is a real address rather
        // than a click handler on a wrapper - the middle button and Ctrl+click work the way they do
        // everywhere else (see conventions/pwa-copy-and-card-links.md).
        Assert.Contains(
            "Href=\"@CardHref(board.Project.Handle, item.Reference.CardId)\"",
            page,
            StringComparison.Ordinal);
        Assert.Contains(
            "Class=\"aiko-tag aiko-tag--quiet aiko-tag--link\"",
            page,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "<span class=\"aiko-tag aiko-tag--quiet\">@item.Reference.CardId</span>",
            page,
            StringComparison.Ordinal);

        // The Open button follows the same address - one road to the card, not a second one assembled
        // beside it - and that address never comes from the route parameter: it speaks the handle the
        // board snapshot carries, so the link cannot name the project by a GUID.
        Assert.Contains(
            "OpenCard(board.Project.Handle, item.Reference.CardId)",
            page,
            StringComparison.Ordinal);
        Assert.Contains("ProjectRoutes.Card(projectHandle, cardId)", page, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectRoutes.Card(ProjectId", page, StringComparison.Ordinal);

        // The tag scale knows the link variant, so the number keeps the design's chip look on screen and
        // only the hover says there is an address under it.
        var css = File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), "src", "Aiko.Pwa", "wwwroot", "css", "app.css"));
        Assert.Contains(".aiko-tag--link", css, StringComparison.Ordinal);
        Assert.Contains("text-decoration: none", css, StringComparison.Ordinal);
        Assert.Contains("text-decoration: underline", css, StringComparison.Ordinal);
    }

    /// <summary>One of the cockpit's screens, read as it ships.</summary>
    private static string RazorPage(string fileName) =>
        File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), "src", "Aiko.Pwa", "Pages", fileName));

    /// <summary>
    /// Walks up from this assembly to the solution file, the same way the other specs do.
    /// </summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(BacklogScreenSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
