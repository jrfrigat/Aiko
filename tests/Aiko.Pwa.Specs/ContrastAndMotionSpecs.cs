using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// The quiet text of the interface is painted with text roles rather than border roles, and the interface
/// answers the system's own "less motion" setting. Read from the stylesheet by rule, not by substring: the
/// word "outline" appears in many places where it belongs, and finding it anywhere would prove nothing.
/// </summary>
public sealed class ContrastAndMotionSpecs
{
    [Theory]
    [InlineData(".aiko-eyebrow")]
    [InlineData(".aiko-kbd")]
    [InlineData(".aiko-field__label")]
    [InlineData(".aiko-field__trailing")]
    [InlineData(".aiko-field__hint")]
    public void Quiet_text_is_painted_with_a_text_role(string selector)
    {
        var rule = Rule(Css(), selector);

        // The role has to be one of the two text tones the palette verifies; a border or divider tone is
        // what these rules used to carry, and its contrast for 11px text is not guaranteed by anything.
        Assert.True(
            rule.Contains("var(--flare-color-on-surface-variant)", StringComparison.Ordinal) ||
            rule.Contains("var(--flare-color-on-surface-variant-2)", StringComparison.Ordinal),
            $"{selector} should take its colour from a text role:\n{rule}");
        Assert.DoesNotContain("--flare-color-outline", rule, StringComparison.Ordinal);
    }

    [Fact]
    public void The_interface_answers_the_systems_less_motion_setting()
    {
        var css = Css();

        var start = css.IndexOf("@media (prefers-reduced-motion: reduce)", StringComparison.Ordinal);
        Assert.True(start >= 0, "the stylesheet should carry a reduced-motion block");

        var block = css[start..];
        var end = block.IndexOf("\n}", StringComparison.Ordinal);
        block = end < 0 ? block : block[..end];

        // Motion stops; the state it expressed stays. Both a transition and an animation have to be covered,
        // because the interface uses both.
        Assert.Contains("animation-duration", block, StringComparison.Ordinal);
        Assert.Contains("transition-duration", block, StringComparison.Ordinal);
    }

    /// <summary>One rule of the stylesheet: the selector's own block, up to its closing brace.</summary>
    private static string Rule(string css, string selector)
    {
        var start = css.IndexOf($"{selector} {{", StringComparison.Ordinal);
        Assert.True(start >= 0, $"missing rule: {selector}");
        var end = css.IndexOf('}', start);
        Assert.True(end > start, $"unterminated rule: {selector}");
        return css[start..end];
    }

    private static string Css() =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Aiko.Pwa", "wwwroot", "css", "app.css"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>Walks up from this assembly to the solution file, as the other specs do.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(typeof(ContrastAndMotionSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
