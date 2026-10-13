using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// The shared fields and the way into the content are usable without a mouse: a choice is named, a refusal
/// announces itself, and the keyboard can start at the page instead of walking the chrome. Read from the
/// markup and the code, as the other screen specs do, until the PWA has component tests (TASK-216).
/// </summary>
public sealed class FieldAccessibilitySpecs
{
    [Fact]
    public void A_choice_is_named_by_its_label()
    {
        var select = Read("src", "Aiko.Pwa", "Pages", "AikoSelectField.razor");

        // The control is Flare's, so the field hands it the label's text as the accessible name; a caller's
        // own AriaLabel still wins.
        Assert.Contains("AriaLabel=\"@(AriaLabel ?? Label)\"", select, StringComparison.Ordinal);

        // And the label is not a `for` target pointing at the wrapper: that would name a plain div, which no
        // assistive technology reads as a name.
        Assert.DoesNotContain("for=\"@_id\"", select, StringComparison.Ordinal);
        Assert.Contains("<label class=\"aiko-field__label\">@label</label>", select, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("AikoField.razor")]
    [InlineData("AikoSelectField.razor")]
    [InlineData("AikoNumberField.razor")]
    public void A_refused_value_announces_itself(string file)
    {
        var field = Read("src", "Aiko.Pwa", "Pages", file);

        Assert.Contains("class=\"aiko-field__error\" role=\"alert\"", field, StringComparison.Ordinal);
    }

    [Fact]
    public void The_keyboard_can_start_at_the_page()
    {
        var layout = Read("src", "Aiko.Pwa", "Layout", "MainLayout.razor");

        // The first thing the keyboard reaches, aimed at a target that takes focus.
        Assert.Contains("href=\"#aiko-main\"", layout, StringComparison.Ordinal);
        Assert.Contains("Loc.Get(\"SkipToContent\")", layout, StringComparison.Ordinal);
        Assert.Contains("id=\"aiko-main\" tabindex=\"-1\"", layout, StringComparison.Ordinal);

        // The target wraps the page rather than standing beside it, and carries no classes: the pages keep
        // the grid they had. The nesting is what is asserted here, not the indentation: pinning the
        // whitespace is what made this spec fail the moment the shell stopped hiding the page behind a
        // spinner, which says nothing about the skip link.
        var target = Regex.Match(
            layout,
            "<div id=\"aiko-main\" tabindex=\"-1\">(?<page>.*?)</div>",
            RegexOptions.Singleline);
        Assert.True(target.Success, "The skip link's target should be a div the page is drawn inside.");
        Assert.Contains("@Body", target.Groups["page"].Value, StringComparison.Ordinal);
    }

    [Fact]
    public void The_skip_caption_exists_in_both_languages()
    {
        foreach (var file in new[] { "Loc.resx", "Loc.ru.resx" })
        {
            var names = XDocument.Parse(Read("src", "Aiko.Pwa", "Resources", file))
                .Root!.Elements("data").Select(data => (string?)data.Attribute("name"));
            Assert.Contains("SkipToContent", names);
        }
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. segments])).Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>Walks up from this assembly to the solution file, as the other specs do.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(typeof(FieldAccessibilitySpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
