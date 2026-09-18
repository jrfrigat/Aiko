using System.Text.RegularExpressions;
using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// Guards the Razor markup itself. Razor treats text shaped like an email address as plain text, so an
/// expression written straight after a word renders literally: `v@State.System.Version` shipped in
/// v0.3.0 as the version tag in the top bar, and nothing else in the build notices - the page compiles,
/// it just displays source code to the user.
/// </summary>
public sealed class RazorMarkupSpecs
{
    // A word character immediately followed by @ and an identifier. Razor's email heuristic wins in that
    // shape, so the expression is never evaluated.
    private static readonly Regex EmailShapedExpression =
        new(@"[A-Za-z0-9_]@[A-Za-z_]", RegexOptions.Compiled);

    // Razor comments never render, and they are exactly where this pattern gets explained.
    private static readonly Regex RazorComment =
        new(@"@\*.*?\*@", RegexOptions.Compiled | RegexOptions.Singleline);

    [Fact]
    public void No_razor_expression_is_written_where_razor_reads_an_email_address()
    {
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(FindRepositoryRoot(), "src", "Aiko.Pwa"),
                     "*.razor",
                     SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            // Comments are blanked rather than removed so the reported line numbers stay right.
            var text = RazorComment.Replace(
                File.ReadAllText(file),
                match => new string('\n', match.Value.Count(character => character == '\n')));
            var lines = text.Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                if (EmailShapedExpression.IsMatch(lines[index]))
                {
                    offenders.Add($"{file}:{index + 1}: {lines[index].Trim()}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Razor reads these as email-looking text and renders them literally; write the expression as " +
            "@($\"...{value}\") instead:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void Card_tab_captions_count_what_the_page_loaded_and_always_show_the_number()
    {
        var path = Path.Combine(
            FindRepositoryRoot(), "src", "Aiko.Pwa", "Pages", "CardInspector.razor");
        var text = File.ReadAllText(path);

        // One formatter for all three captions, so a count reads the same wherever it is printed.
        Assert.Equal(3, Regex.Matches(text, @"DisplayFormat\.Counted\(").Count);

        // A zero is printed like any other number: the discussion caption used to branch on it and leave the
        // number out. (The panel itself may still branch - an empty feed needs its own words.)
        var discussionCaption = Regex.Match(text, @"private string DiscussionTabLabel =>(?<body>[^;]*);");
        Assert.True(discussionCaption.Success, "The discussion caption should stay a one-expression property.");
        Assert.Contains(
            "DisplayFormat.Counted(",
            discussionCaption.Groups["body"].Value,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Count == 0",
            discussionCaption.Groups["body"].Value,
            StringComparison.Ordinal);

        // The counts come from what the page loaded, not from the tab the user opened - switching tabs only
        // remembers the index, which is why the numbers used to appear "after visiting the tab".
        var switched = Regex.Match(text, @"private void OnActiveTabChanged\(int index\) =>(?<body>[^;]*);");
        Assert.True(switched.Success, "OnActiveTabChanged should stay a one-liner that records the index.");
        Assert.DoesNotContain("Load", switched.Groups["body"].Value, StringComparison.Ordinal);

        // Flare draws the tab bar from its children's labels before it diffs those children, so the captions of
        // a changed count need one extra pass to reach the bar - the page asks for it explicitly.
        Assert.Contains("protected override void OnAfterRender(bool firstRender)", text, StringComparison.Ordinal);
        Assert.Contains("TabCountsSignature", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Card_texts_are_behind_three_tabs_with_stable_field_names()
    {
        var path = Path.Combine(
            FindRepositoryRoot(), "src", "Aiko.Pwa", "Pages", "CardInspector.razor");
        var text = File.ReadAllText(path);

        // One bar of three text tabs, each labelled from the resources: the card's prose had grown into three
        // long editors stacked on top of each other, and a tab nobody can reach is a field that does not exist.
        Assert.Contains(
            "<FlareTabs ActiveIndex=\"_textTab\" ActiveIndexChanged=\"OnTextTabChanged\">",
            text,
            StringComparison.Ordinal);
        Assert.Contains("<FlareTab Label=\"@Loc.Get(\"Request\")\">", text, StringComparison.Ordinal);
        Assert.Contains("<FlareTab Label=\"@Loc.Get(\"Requirements\")\">", text, StringComparison.Ordinal);
        Assert.Contains("<FlareTab Label=\"@Loc.Get(\"DeclaredScope\")\">", text, StringComparison.Ordinal);

        // The editors keep the names they were copied into the form under; the request is the one that is new.
        Assert.Contains("{Card.Reference.CardId}.request", text, StringComparison.Ordinal);
        Assert.Contains("{Card.Reference.CardId}.requirements", text, StringComparison.Ordinal);
        Assert.Contains("{Card.Reference.CardId}.scope", text, StringComparison.Ordinal);

        // What was typed lives in the page's fields, not in the tab, so switching tabs cannot drop it ...
        Assert.Contains("Value=\"@_request\"", text, StringComparison.Ordinal);
        Assert.Contains("Value=\"@_requirements\"", text, StringComparison.Ordinal);
        Assert.Contains("_request = Card.Request;", text, StringComparison.Ordinal);

        // ... and saving sends all three texts, the request included - a tab whose content the save drops would
        // be decoration.
        Assert.Contains("CriterionPayload, _requirements, _request)", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Walks up from this assembly to the solution file, the same way the daemon fixture does.
    /// </summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(RazorMarkupSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
