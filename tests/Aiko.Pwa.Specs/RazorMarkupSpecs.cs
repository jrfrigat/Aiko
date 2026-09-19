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

    [Fact]
    public void Code_changes_open_file_by_file_behind_a_toggle()
    {
        var root = FindRepositoryRoot();
        var text = File.ReadAllText(
            Path.Combine(root, "src", "Aiko.Pwa", "Pages", "CardInspector.razor"));

        // The diff is a list of collapsible panels, one per file, and several may stand open at once: a card
        // that touched a dozen files used to print every patch in one unbroken run of text.
        Assert.Contains("<FlareAccordion AllowMultiple=\"true\"", text, StringComparison.Ordinal);
        Assert.Contains("<FlareAccordionPanel Expanded=\"@expanded\"", text, StringComparison.Ordinal);
        Assert.Contains(
            "ExpandedChanged=\"@(value => SetFileExpanded(entry.Path, value))\"",
            text,
            StringComparison.Ordinal);

        // The toggle's header is the file's own row - its path and what the patch does to it ...
        Assert.Contains("class=\"aiko-diff__file\"", text, StringComparison.Ordinal);
        Assert.Contains("class=\"aiko-diff__path\"", text, StringComparison.Ordinal);

        // ... and the patch stands inside the panel, drawn only while that file is open. The wall of text is
        // not merely hidden: a collapsed file renders nothing.
        Assert.Contains("@if (expanded && entry.Patch is { Length: > 0 } patch)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("@if (entry.Patch is", text, StringComparison.Ordinal);
        Assert.Contains("aiko-diff__line aiko-diff__line--@DiffLineKind(text)", text, StringComparison.Ordinal);

        // Which files are open lives on the page, keyed by path, so opening one cannot close another and the
        // panel's own re-render cannot fold the patch being read.
        Assert.Contains("private readonly HashSet<string> _expandedFiles", text, StringComparison.Ordinal);
        Assert.Contains("_expandedFiles.Add(path);", text, StringComparison.Ordinal);

        // The row is laid out by the cockpit's own stylesheet, which is the only place that sees the header
        // button Flare draws around it.
        var css = File.ReadAllText(
            Path.Combine(root, "src", "Aiko.Pwa", "wwwroot", "css", "app.css"));
        Assert.Contains(".aiko-diff__file {", css, StringComparison.Ordinal);
        Assert.Contains(".aiko-diff__path {", css, StringComparison.Ordinal);
    }

    [Fact]
    public void A_card_reported_from_another_project_shows_where_it_came_from()
    {
        var root = FindRepositoryRoot();
        var text = File.ReadAllText(
            Path.Combine(root, "src", "Aiko.Pwa", "Pages", "CardInspector.razor"));

        // The block exists and is driven by the domain's own provenance: a card that came from nowhere
        // renders nothing, so the panel is a fact about the card rather than a permanent fixture.
        Assert.Contains("@if (Card.Origin is { } origin)", text, StringComparison.Ordinal);

        // The source project is matched by its immutable id: that is what the card stores, while every
        // link the UI builds carries the readable handle.
        Assert.Contains(
            "StringComparer.Ordinal.Equals(project.Id, origin.ProjectId)",
            text,
            StringComparison.Ordinal);

        // A link to the originating card needs both halves - a registered project and a named source card -
        // and without either the block states the project and links nowhere. A route built from a handle
        // that does not exist would be a page nobody can open.
        Assert.Contains(
            "Card.Origin is { CardId: { Length: > 0 } originCardId } && OriginProject is { } project",
            text,
            StringComparison.Ordinal);
        Assert.Contains("@if (OriginCardHref is { Length: > 0 } originHref)", text, StringComparison.Ordinal);
        // The address comes from the one helper that speaks handles, with the project's readable handle and
        // the source card's id - never with the id the card's own reference stores.
        Assert.Contains("ProjectRoutes.Card(project.Handle, originCardId)", text, StringComparison.Ordinal);

        // The captions are keys, and both languages carry them - a block that reads English on a Russian
        // screen is the failure this pair of assertions exists to catch.
        Assert.Contains("Loc.Get(\"CardOriginTitle\")", text, StringComparison.Ordinal);
        Assert.Contains("Loc.Get(\"CardOriginTag\")", text, StringComparison.Ordinal);
        foreach (var resource in new[] { "Loc.resx", "Loc.ru.resx" })
        {
            var resx = File.ReadAllText(
                Path.Combine(root, "src", "Aiko.Pwa", "Resources", resource));
            Assert.Contains("name=\"CardOriginTitle\"", resx, StringComparison.Ordinal);
            Assert.Contains("name=\"CardOriginTag\"", resx, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_related_card_shows_its_current_status_the_way_the_board_draws_one()
    {
        var root = FindRepositoryRoot();
        var cardPage = File.ReadAllText(
            Path.Combine(root, "src", "Aiko.Pwa", "Pages", "CardInspector.razor"));

        // The related-cards row asks for the other card's state and draws it with the one badge the rest of
        // the product uses for a status - asking is not optional, and neither is the shared view.
        Assert.Contains("RelatedStageState(link)", cardPage, StringComparison.Ordinal);
        Assert.Contains("aiko-tag @StageStateView.TagClass(relatedState)", cardPage, StringComparison.Ordinal);
        Assert.Contains("Loc.Get(StageStateView.LabelKey(relatedState))", cardPage, StringComparison.Ordinal);

        // A card the snapshot does not hold is left without a badge: "nothing known" is not "pending".
        Assert.Contains("related is null", cardPage, StringComparison.Ordinal);

        // The state is derived in one place. Both screens go through it, and the board no longer filters the
        // runs itself - that second copy is what let the two screens drift apart.
        Assert.Contains(
            "StageStateView.Of(Board.StageRuns, related.Reference.CardId, related.StageId)",
            cardPage,
            StringComparison.Ordinal);
        var board = File.ReadAllText(
            Path.Combine(root, "src", "Aiko.Pwa", "Pages", "BoardSection.razor"));
        Assert.Contains(
            "StageStateView.Of(Board.StageRuns, card.Reference.CardId, card.StageId)",
            board,
            StringComparison.Ordinal);
        Assert.DoesNotContain("run.CardId", board, StringComparison.Ordinal);

        var stateView = File.ReadAllText(
            Path.Combine(root, "src", "Aiko.Pwa", "Services", "StageStateView.cs"));
        Assert.Contains(
            "public static StageState Of(IReadOnlyList<StageRunSummary>? runs, string cardId, string stageId)",
            stateView,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_file_tabs_end_with_the_files_that_fell_outside_the_declared_scope()
    {
        var root = FindRepositoryRoot();
        var text = File.ReadAllText(
            Path.Combine(root, "src", "Aiko.Pwa", "Pages", "CardInspector.razor"));

        // The third tab is the deviation, not a union of everything: it is the list a reader checks a card by,
        // and the same one the footer's DoD line counts.
        Assert.Contains("Loc.Get(\"OutOfScope\")", text, StringComparison.Ordinal);
        Assert.Contains("Files=\"BoardSection.FindOutOfScopeFiles(Card)\"", text, StringComparison.Ordinal);
        Assert.Contains("Warning=\"true\"", text, StringComparison.Ordinal);

        // The union and its caption are gone: a tab that says "all scope" while the deviation is what matters
        // is the lie this replaced.
        Assert.DoesNotContain("AllScope", text, StringComparison.Ordinal);
        Assert.DoesNotContain("private IReadOnlyList<string> ScopeFiles", text, StringComparison.Ordinal);

        // The caption lives in both languages, and the old key has left both.
        foreach (var resource in new[] { "Loc.resx", "Loc.ru.resx" })
        {
            var resx = File.ReadAllText(
                Path.Combine(root, "src", "Aiko.Pwa", "Resources", resource));
            Assert.Contains("name=\"OutOfScope\"", resx, StringComparison.Ordinal);
            Assert.DoesNotContain("name=\"AllScope\"", resx, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_linked_projects_registry_has_a_rail_item_and_a_screen_of_its_own()
    {
        var root = FindRepositoryRoot();
        var rail = File.ReadAllText(
            Path.Combine(root, "src", "Aiko.Pwa", "Layout", "MainLayout.razor"));
        var page = File.ReadAllText(
            Path.Combine(root, "src", "Aiko.Pwa", "Pages", "LinkedProjectsPage.razor"));
        var settings = File.ReadAllText(
            Path.Combine(root, "src", "Aiko.Pwa", "Pages", "ProjectSettingsPage.razor"));

        // The rail leads to the registry, in the project's own group and addressed by its readable handle -
        // the same shape every other project screen uses.
        Assert.Contains("SectionHref(open.Handle, \"links\")", rail, StringComparison.Ordinal);
        Assert.Contains("Loc.Get(\"LinkedProjectsNav\")", rail, StringComparison.Ordinal);

        // The screen owns the route and the whole registry: the list, adding a link ...
        Assert.Contains("@page \"/p/{ProjectId}/links\"", page, StringComparison.Ordinal);
        Assert.Contains("Loc.Get(\"LinkedProjectsNone\")", page, StringComparison.Ordinal);
        Assert.Contains("AddLinkAsync", page, StringComparison.Ordinal);
        Assert.Contains("RemoveLinkAsync(item)", page, StringComparison.Ordinal);

        // ... and correcting a description, which is the half the settings panel never had: a row opens for
        // editing, saves with its own button and can be abandoned.
        Assert.Contains("BeginEdit(item)", page, StringComparison.Ordinal);
        Assert.Contains("SaveDescriptionAsync(item)", page, StringComparison.Ordinal);
        Assert.Contains("CancelEdit", page, StringComparison.Ordinal);
        Assert.Contains("Loc.Get(\"LinkedProjectsEdit\")", page, StringComparison.Ordinal);

        // The project is excluded by either of its names, because the route carries the handle while the
        // registry stores the id: matching only one of them would offer the project itself for linking.
        Assert.Contains("StringComparer.Ordinal.Equals(project.Handle, ProjectId)", page, StringComparison.Ordinal);
        Assert.Contains("StringComparer.Ordinal.Equals(project.Id, ProjectId)", page, StringComparison.Ordinal);

        // The settings page points at the screen instead of carrying a second editor of the same file, and
        // the pointer is built from the handle rather than from the id.
        Assert.Contains("LinksHref(board.Project.Handle)", settings, StringComparison.Ordinal);
        Assert.Contains("Loc.Get(\"LinkedProjectsOpen\")", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("LinkedProjectsAdd", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("AddLinkAsync", settings, StringComparison.Ordinal);

        // Every caption the rail and the screen use exists in both languages.
        string[] keys =
        [
            "LinkedProjectsNav",
            "LinkedProjectsMovedHint",
            "LinkedProjectsOpen",
            "LinkedProjectsEdit",
            "LinkedProjectsSave",
            "LinkedProjectsCancel"
        ];
        foreach (var resource in new[] { "Loc.resx", "Loc.ru.resx" })
        {
            var resx = File.ReadAllText(
                Path.Combine(root, "src", "Aiko.Pwa", "Resources", resource));
            foreach (var key in keys)
            {
                Assert.Contains($"name=\"{key}\"", resx, StringComparison.Ordinal);
            }
        }
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
