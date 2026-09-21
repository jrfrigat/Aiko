using System.Text.RegularExpressions;
using Aiko.Domain.Cards;
using Aiko.Domain.Workflow;
using Aiko.Pwa.Layout;
using Aiko.Pwa.Services;
using Flare.Components;
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
    public void The_board_lists_the_runs_the_project_is_working_on()
    {
        var root = FindRepositoryRoot();
        var board = File.ReadAllText(
            Path.Combine(root, "src", "Aiko.Pwa", "Pages", "BoardView.razor"));

        // The panel reads the project's own stage runs to learn which pairs are unfinished, and asks a card for
        // its details only when that card is one of them - walking every card is what the requirement forbids.
        Assert.Contains("board.StageRuns", board, StringComparison.Ordinal);
        Assert.Contains("ActiveRunStates", board, StringComparison.Ordinal);
        Assert.Contains("/cards/{Uri.EscapeDataString(cardId)}/executions", board, StringComparison.Ordinal);

        // A row opens the card through the canonical address, so the link stays shareable.
        Assert.Contains("OpenCard(row.CardId)", board, StringComparison.Ordinal);

        // The captions live in both languages.
        foreach (var resource in new[] { "Loc.resx", "Loc.ru.resx" })
        {
            var resx = File.ReadAllText(
                Path.Combine(root, "src", "Aiko.Pwa", "Resources", resource));
            foreach (var key in new[] { "ActiveRunsTitle", "ActiveRunsNone", "ActiveRunsNoAgent" })
            {
                Assert.Contains($"name=\"{key}\"", resx, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void The_card_page_steers_a_run_through_the_execution_endpoints()
    {
        var root = FindRepositoryRoot();
        var inspector = File.ReadAllText(
            Path.Combine(root, "src", "Aiko.Pwa", "Pages", "CardInspector.razor"));

        // Every life-cycle action the daemon offers has a control on the runs tab, and all of them are built
        // from one address helper: six hand-written URLs would let one of them drift from the daemon's routes.
        Assert.Contains("ExecutionAction(run.Id, action)", inspector, StringComparison.Ordinal);
        foreach (var action in new[]
                 {
                     "pause", "resume", "handoff", "complete", "cancel", "scope-response", "commit-approval"
                 })
        {
            Assert.Contains($"\"{action}\"", inspector, StringComparison.Ordinal);
        }

        // Starting a stage addresses the card's own executions - the same collection the agent's tool creates -
        // and the panel names the run it acts on, so a click cannot hit the wrong one silently.
        Assert.Contains(
            "/cards/{Uri.EscapeDataString(Card.Reference.CardId)}/executions",
            inspector,
            StringComparison.Ordinal);
        Assert.Contains("StartStageRequest(Card.StageId", inspector, StringComparison.Ordinal);

        // The captions live in both languages, and the failure text goes through the shared reader.
        foreach (var resource in new[] { "Loc.resx", "Loc.ru.resx" })
        {
            var resx = File.ReadAllText(
                Path.Combine(root, "src", "Aiko.Pwa", "Resources", resource));
            foreach (var key in new[]
                     {
                         "ExecutionActions", "StartStage", "PauseRun", "ResumeRun", "CancelRun", "CompleteRun",
                         "HandoffRun", "ApproveScope", "RefuseScope", "ApproveCommit", "RejectCommit",
                         "ExecutionActionFailed"
                     })
            {
                Assert.Contains($"name=\"{key}\"", resx, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void The_daemon_screen_surfaces_the_drift_the_doctor_finds_and_not_a_second_check()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(
            Path.Combine(root, "src", "Aiko.Pwa", "Pages", "DaemonPage.razor"));

        // An agent configuration that points at an old port is the doctor's finding, and this screen shows
        // the doctor's own list: it asks the diagnostics endpoint and filters by the shared area constant
        // instead of deciding staleness here, where the two rules would drift apart.
        Assert.Contains("api/v1/system/diagnostics", page, StringComparison.Ordinal);
        Assert.Contains("DiagnosticFinding.AgentConfigArea", page, StringComparison.Ordinal);
        Assert.Contains("AgentConfigDrift", page, StringComparison.Ordinal);

        // The other failure the same inspection reports: the entry is present and current, so the agent reads
        // as connected, while the credential it names cannot be used. Its own area, so the drift wording
        // above cannot be printed about it.
        Assert.Contains("DiagnosticFinding.AgentCredentialArea", page, StringComparison.Ordinal);
        Assert.Contains("AgentCredentialProblem", page, StringComparison.Ordinal);

        // Both texts each block uses exist in both languages.
        foreach (var resource in new[] { "Loc.resx", "Loc.ru.resx" })
        {
            var resx = File.ReadAllText(
                Path.Combine(root, "src", "Aiko.Pwa", "Resources", resource));
            foreach (var key in new[]
                     {
                         "AgentConfigDrift",
                         "AgentConfigDriftHint",
                         "AgentCredentialProblem",
                         "AgentCredentialProblemHint"
                     })
            {
                Assert.Contains($"name=\"{key}\"", resx, StringComparison.Ordinal);
            }
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
        Assert.Contains("SaveLinkAsync(item)", page, StringComparison.Ordinal);
        Assert.Contains("CancelEdit", page, StringComparison.Ordinal);
        Assert.Contains("Loc.Get(\"LinkedProjectsEdit\")", page, StringComparison.Ordinal);

        // A link is more than its sentence: where the neighbour's reference lives, and when work does and does
        // not belong there, are edited on the same screen - in the form that adds a link and in the row that
        // corrects one - because a reference an agent cannot read is a reference nobody wrote down.
        Assert.Contains("LinkedProjectsReference", page, StringComparison.Ordinal);
        Assert.Contains("LinkedProjectsWhenToUse", page, StringComparison.Ordinal);
        Assert.Contains("LinkedProjectsWhenNotToUse", page, StringComparison.Ordinal);
        Assert.Contains("Value=\"@_linkReference\"", page, StringComparison.Ordinal);
        Assert.Contains("Value=\"@_editingReference\"", page, StringComparison.Ordinal);
        Assert.Contains("new LinkProjectRequest(description, reference, whenToUse, whenNotToUse)", page, StringComparison.Ordinal);

        // A row shows only what the entry carries, so a link written before these texts existed reads exactly
        // as it did - its description and nothing else.
        Assert.Contains("private static IReadOnlyList<string> Notes(ProjectLink link)", page, StringComparison.Ordinal);
        Assert.Contains("if (!string.IsNullOrWhiteSpace(value))", page, StringComparison.Ordinal);

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
            "LinkedProjectsCancel",
            "LinkedProjectsReference",
            "LinkedProjectsReferencePlaceholder",
            "LinkedProjectsWhenToUse",
            "LinkedProjectsWhenToUsePlaceholder",
            "LinkedProjectsWhenNotToUse",
            "LinkedProjectsWhenNotToUsePlaceholder"
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

    [Fact]
    public void The_appearance_screen_lists_the_languages_from_code_and_none_of_them_by_hand()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(
            Path.Combine(root, "src", "Aiko.Pwa", "Pages", "AppearancePage.razor"));

        // The screen owns the route and offers the choice from the one list in code. The names come from
        // ICU, so no language needs a resource key of its own either.
        Assert.Contains("@page \"/appearance\"", page, StringComparison.Ordinal);
        Assert.Contains("Items=\"@UiLanguages.Supported\"", page, StringComparison.Ordinal);
        Assert.Contains("ItemLabel=\"@UiLanguages.DisplayName\"", page, StringComparison.Ordinal);

        // A language spelled out in the markup would be a second copy of the list, and the copy nobody
        // updates: adding Loc.<culture>.resx has to stay enough.
        Assert.DoesNotContain("\"en\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ru\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<option", page, StringComparison.Ordinal);

        // The choice is stored under the key the .NET side reads, and only then does the page reload -
        // the decision STORY-14's analysis records, and the order that makes it work.
        Assert.Contains("localStorage.setItem", page, StringComparison.Ordinal);
        Assert.Contains("UiLanguages.PreferenceKey", page, StringComparison.Ordinal);
        Assert.Contains("forceLoad: true", page, StringComparison.Ordinal);

        // The rail leads to it, in the installation's own group.
        var rail = File.ReadAllText(
            Path.Combine(root, "src", "Aiko.Pwa", "Layout", "MainLayout.razor"));
        Assert.Contains("Href=\"/appearance\"", rail, StringComparison.Ordinal);
        Assert.Contains("Loc.Get(\"AppearanceNav\")", rail, StringComparison.Ordinal);

        // Every caption the rail and the screen use exists in both languages.
        string[] keys =
        [
            "AppearanceNav",
            "AppearanceTitle",
            "AppearanceHint",
            "AppearanceLanguage",
            "AppearanceLanguageHint",
            "AppearanceReloadHint"
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

    [Fact]
    public void The_appearance_screen_offers_the_three_modes_the_theme_service_knows()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(
            Path.Combine(root, "src", "Aiko.Pwa", "Pages", "AppearancePage.razor"));

        // One control with one value out of three. FlareColorModeToggle is a boolean and cannot express
        // "auto", and an "auto" option beside it would be a second control on the same axis - the two
        // could then contradict each other, and the person would not know what they had changed.
        Assert.Contains("FlareToggleGroup TValue=\"ThemeMode\"", page, StringComparison.Ordinal);
        Assert.Contains("Mandatory=\"true\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<FlareColorModeToggle", page, StringComparison.Ordinal);

        // The states are the enum's values rather than strings, and the choice goes to the service: the
        // provider is what remembers it, not this screen.
        Assert.Contains("Value=\"@ThemeMode.Auto\"", page, StringComparison.Ordinal);
        Assert.Contains("Value=\"@ThemeMode.Light\"", page, StringComparison.Ordinal);
        Assert.Contains("Value=\"@ThemeMode.Dark\"", page, StringComparison.Ordinal);
        Assert.Contains("ValueChanged=\"SetModeAsync\"", page, StringComparison.Ordinal);
        Assert.Contains("ThemeService.SetModeAsync(", page, StringComparison.Ordinal);

        // Not offered while the palette carries two schemes and nothing would answer the fourth state.
        Assert.DoesNotContain("HighContrast", page, StringComparison.Ordinal);

        // Every caption exists in both languages.
        string[] keys =
        [
            "AppearanceMode",
            "AppearanceModeHint",
            "AppearanceModeAuto",
            "AppearanceModeLight",
            "AppearanceModeDark"
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

    [Fact]
    public void The_appearance_screen_offers_every_palette_the_theme_ships()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(
            Path.Combine(root, "src", "Aiko.Pwa", "Pages", "AppearancePage.razor"));

        // The list is the theme's, not a constant and not markup: IThemeService.Palettes is what a theme
        // ships, so a fourth palette is added in the theme and appears on this screen untouched.
        Assert.Contains("Items=\"@PaletteIds\"", page, StringComparison.Ordinal);
        Assert.Contains("ThemeService.Palettes", page, StringComparison.Ordinal);

        // A palette names itself - the row reads the Palette's own Name and Source - so no palette name is
        // written out here and none needs a resource key that would then need translating.
        Assert.Contains("palette.Name", page, StringComparison.Ordinal);
        Assert.Contains("palette.Source", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Kinetic Orchestration", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Cyber Amber", page, StringComparison.Ordinal);

        // The value is the id: it is what SetPaletteAsync takes and what the provider stores, while the
        // label is what the reader sees. Strongly-typed args, not a hand-written list of options.
        Assert.Contains("ItemLabel=\"@PaletteLabel\"", page, StringComparison.Ordinal);
        Assert.Contains("ThemeService.CurrentPalette.Id", page, StringComparison.Ordinal);
        Assert.Contains("ThemeService.SetPaletteAsync(", page, StringComparison.Ordinal);

        // FlareColorCustomizer repaints one role - the "paint the roles yourself" axis, which is outside
        // this story - so it is not this screen's control.
        Assert.DoesNotContain("<FlareColorCustomizer", page, StringComparison.Ordinal);

        // Every caption the axis uses exists in both languages.
        string[] keys =
        [
            "AppearancePalette",
            "AppearancePaletteHint"
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

    [Fact]
    public void The_command_block_is_a_panel_of_its_own_between_the_state_and_the_triage()
    {
        var text = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "Aiko.Pwa", "Pages", "CardInspector.razor"));

        var state = text.IndexOf("@Loc.Get(\"ExecutionState\")", StringComparison.Ordinal);
        var command = text.IndexOf("@Loc.Get(\"CommandsTitle\")", StringComparison.Ordinal);
        var triage = text.IndexOf("@Loc.Get(\"TriageScore\")", StringComparison.Ordinal);

        // The order on the page is the order in the markup: the state, then the command, then the triage the
        // command used to be drawn inside of.
        Assert.True(
            state >= 0 && command > state && triage > command,
            "The command block should stand between the execution state and the triage panel.");

        // Its own container rather than the triage's: the nearest panel opening above the command title has
        // no trace of the triage header after it, so the two headings belong to different panels.
        var panel = text.LastIndexOf("<FlarePaper", command, StringComparison.Ordinal);
        Assert.True(panel >= 0, "The command block should sit inside a panel.");
        Assert.DoesNotContain("TriageScore", text[panel..command], StringComparison.Ordinal);

        // And that panel closes before the save button: saving the card is not part of placing a command.
        var closes = text.IndexOf("</FlarePaper>", command, StringComparison.Ordinal);
        var save = text.IndexOf("<FlareButton FullWidth=\"true\"", command, StringComparison.Ordinal);
        Assert.True(
            closes > 0 && save > closes,
            "The save button belongs to the triage panel, not to the command block.");
    }

    [Fact]
    public void The_card_banner_reports_the_effective_priority_the_way_the_panel_does()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "src", "Aiko.Pwa", "Pages", "CardPage.razor"));
        var inspector = File.ReadAllText(
            Path.Combine(root, "src", "Aiko.Pwa", "Pages", "CardInspector.razor"));

        // The fifth counter: the label the dictionary already carries, and the figure the formula produces.
        Assert.Contains(
            "(Loc.Get(\"EstimateTitle\"), DisplayFormat.Priority(effective), false)",
            page,
            StringComparison.Ordinal);

        // The same entry, the same fallback and the same formatter as the card's own panel - one page
        // stating one figure in two ways is what adding the counter was meant to avoid.
        Assert.Contains("?.Snapshot.EffectivePriority ?? card.OwnPriority", page, StringComparison.Ordinal);
        Assert.Contains("DisplayFormat.Priority(", inspector, StringComparison.Ordinal);
    }


    [Fact]
    public void The_card_number_is_a_copy_control_that_copies_the_id_without_the_hash()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(
            Path.Combine(root, "src", "Aiko.Pwa", "Pages", "CardPage.razor"));

        // Pressing the header's number copies the id a command names the card by - without the "#", which
        // is only how the screen writes it - through the one control in this product that copies ...
        Assert.Contains("<FlareClipboard Text=\"@Card.Reference.CardId\"", page, StringComparison.Ordinal);
        Assert.Contains("Variant=\"ButtonVariant.Text\"", page, StringComparison.Ordinal);

        // ... while the screen keeps writing the number with its hash, and the press says what it did.
        Assert.Contains("@($\"#{Card.Reference.CardId}\")", page, StringComparison.Ordinal);
        Assert.Contains("<FeedbackContent>", page, StringComparison.Ordinal);

        // A second way of copying is what this replaced, so there is none: the client never reaches for
        // the browser's own clipboard API.
        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(root, "src", "Aiko.Pwa"), "*.razor", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(
                         Path.Combine(root, "src", "Aiko.Pwa"), "*.cs", SearchOption.AllDirectories)))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            Assert.DoesNotContain("navigator.clipboard", File.ReadAllText(file), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_command_block_offers_the_two_run_commands_with_this_card_number()
    {
        var root = FindRepositoryRoot();
        var text = File.ReadAllText(
            Path.Combine(root, "src", "Aiko.Pwa", "Pages", "CardInspector.razor"));

        // Both examples carry this card's own number, built the way the estimate's command is built ...
        Assert.Contains("$\"/aiko-run {Card.Reference.CardId}\"", text, StringComparison.Ordinal);
        Assert.Contains("$\"/aiko-run {Card.Reference.CardId} --all\"", text, StringComparison.Ordinal);
        Assert.Contains("private IReadOnlyList<string> RunCommands =>", text, StringComparison.Ordinal);

        // ... and they are drawn through the copy control, so pressing one takes the command with it.
        Assert.Contains("FlareClipboard Text=\"@example\"", text, StringComparison.Ordinal);
        Assert.Contains("Loc.Get(\"CommandExamples\")", text, StringComparison.Ordinal);

        // The block this belongs to is the one that places a request, so the examples stand inside it and
        // before the panel closes.
        var command = text.IndexOf("Loc.Get(\"CommandsTitle\")", StringComparison.Ordinal);
        var examples = text.IndexOf("RunCommands", StringComparison.Ordinal);
        var closes = text.IndexOf("</FlarePaper>", command, StringComparison.Ordinal);
        Assert.True(
            command >= 0 && examples > command && closes > examples,
            "The run examples should stand inside the command block.");
    }

    [Fact]
    public void A_related_card_states_the_stage_it_stands_in()
    {
        var root = FindRepositoryRoot();
        var text = File.ReadAllText(
            Path.Combine(root, "src", "Aiko.Pwa", "Pages", "CardInspector.razor"));

        // The row asks for the related card's stage and draws it as a tag ...
        Assert.Contains("RelatedStageCaption(link)", text, StringComparison.Ordinal);
        Assert.Contains("private string? RelatedStageCaption(CardRelation relation)", text, StringComparison.Ordinal);
        Assert.Contains("<span class=\"aiko-tag aiko-tag--quiet\">@relatedStage</span>", text, StringComparison.Ordinal);

        // ... named from the card's own workflow, which is what the card's header reads as well - a stage
        // the workflow no longer holds falls back to its id rather than to a translated constant.
        Assert.Contains(
            "?.Stages.FirstOrDefault(stage => StringComparer.Ordinal.Equals(stage.Id, related.StageId))",
            text,
            StringComparison.Ordinal);
        Assert.Contains("?.Title ?? related.StageId;", text, StringComparison.Ordinal);

        // A relation to a card the snapshot does not hold says nothing - neither a stage nor a state.
        Assert.Contains("if (RelatedCard(relation) is not { } related)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_board_opens_a_card_by_its_number_and_by_nothing_else()
    {
        var root = FindRepositoryRoot();
        var board = File.ReadAllText(
            Path.Combine(root, "src", "Aiko.Pwa", "Pages", "BoardSection.razor"));

        // The whole card is no longer a handler: no wrapper opens it, and nothing here raises a selection.
        Assert.DoesNotContain("aiko-card__open", board, StringComparison.Ordinal);
        Assert.DoesNotContain("OnCardSelected", board, StringComparison.Ordinal);

        // The number is a real link, built by the one helper that speaks routes, and by the project's
        // readable handle - the shape that makes the middle button and Ctrl+click work.
        Assert.Contains("<FlareNavLink Href=\"@CardHref(card)\" Class=\"aiko-card__id\">", board, StringComparison.Ordinal);
        Assert.Contains("ProjectRoutes.Card(Board.Project.Handle, card.Reference.CardId)", board, StringComparison.Ordinal);

        // The board page keeps no way of opening a card of its own; the link is the only one left.
        var view = File.ReadAllText(
            Path.Combine(root, "src", "Aiko.Pwa", "Pages", "BoardView.razor"));
        Assert.DoesNotContain("OnCardSelected", view, StringComparison.Ordinal);

        // And the stylesheet says the same: the number looks like a link, the card keeps the plain cursor.
        var css = File.ReadAllText(
            Path.Combine(root, "src", "Aiko.Pwa", "wwwroot", "css", "app.css"));
        Assert.Contains(".aiko-card__id {", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".aiko-card__open", css, StringComparison.Ordinal);
    }

    [Fact]
    public void The_command_example_caption_is_in_both_languages()
    {
        var root = FindRepositoryRoot();
        foreach (var resource in new[] { "Loc.resx", "Loc.ru.resx" })
        {
            var resx = File.ReadAllText(
                Path.Combine(root, "src", "Aiko.Pwa", "Resources", resource));
            Assert.Contains("name=\"CommandExamples\"", resx, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_cross_project_settings_state_which_direction_each_one_is()
    {
        var root = FindRepositoryRoot();
        var editor = File.ReadAllText(
            Path.Combine(root, "src", "Aiko.Pwa", "Pages", "SettingsEditor.razor"));

        // Two settings of opposite direction used to share one heading, and only a hint said which was which - so
        // a person a *sending* project had refused read the *receiving* project's screen, found it correct, and
        // looked no further. Each direction now names itself ...
        Assert.Contains("Loc.Get(\"CrossProjectWrite\")", editor, StringComparison.Ordinal);
        Assert.Contains("Loc.Get(\"CrossProjectTargets\")", editor, StringComparison.Ordinal);

        // ... the list of projects this one may write to says what an empty list means, which the old block left
        // to a placeholder to imply ...
        Assert.Contains("Loc.Get(\"CrossProjectTargetsHint\")", editor, StringComparison.Ordinal);

        // ... and the title that stood over both of them is gone rather than repeated above the pair.
        Assert.DoesNotContain("Loc.Get(\"CrossProjectPolicy\")", editor, StringComparison.Ordinal);

        foreach (var resource in new[] { "Loc.resx", "Loc.ru.resx" })
        {
            var resx = File.ReadAllText(
                Path.Combine(root, "src", "Aiko.Pwa", "Resources", resource));
            Assert.Contains("name=\"CrossProjectWrite\"", resx, StringComparison.Ordinal);
            Assert.Contains("name=\"CrossProjectPolicyHint\"", resx, StringComparison.Ordinal);
            Assert.Contains("name=\"CrossProjectTargets\"", resx, StringComparison.Ordinal);
            Assert.Contains("name=\"CrossProjectTargetsHint\"", resx, StringComparison.Ordinal);
            Assert.DoesNotContain("name=\"CrossProjectPolicy\"", resx, StringComparison.Ordinal);
        }

        // The hint that said the opposite is gone from both languages. It claimed the setting stated what another
        // project may do *here*, while the daemon reads this project's own policy when it writes elsewhere and
        // never consults the receiving project's settings - which is how a person refused by a neighbour's list
        // ended up reading this screen and finding it correct.
        var english = File.ReadAllText(
            Path.Combine(root, "src", "Aiko.Pwa", "Resources", "Loc.resx"));
        var russian = File.ReadAllText(
            Path.Combine(root, "src", "Aiko.Pwa", "Resources", "Loc.ru.resx"));
        Assert.DoesNotContain("may do to this one", english, StringComparison.Ordinal);
        Assert.DoesNotContain("Что другой проект может сделать с этим", russian, StringComparison.Ordinal);
        Assert.Contains("working here", english, StringComparison.Ordinal);
        Assert.Contains("работающий здесь", russian, StringComparison.Ordinal);
    }

    [Fact]
    public void The_rail_names_the_library_with_a_link_to_its_repository_and_the_version_the_client_loaded()
    {
        var root = FindRepositoryRoot();
        var layout = File.ReadAllText(
            Path.Combine(root, "src", "Aiko.Pwa", "Layout", "MainLayout.razor"));

        // The name opens the library's repository: a plain anchor in a new tab. FlareNavLink is the app's
        // own route control - it marks the active route, and an address outside the app has none to mark.
        var link = Regex.Match(
            layout,
            @"<a(?<attributes>[^>]*)>(?<content>.*?)</a>",
            RegexOptions.Singleline);
        Assert.True(link.Success, "The library's name should be drawn as an anchor.");
        Assert.Contains("Flare.Blazor", link.Groups["content"].Value, StringComparison.Ordinal);
        Assert.Contains("target=\"_blank\"", link.Groups["attributes"].Value, StringComparison.Ordinal);
        Assert.Contains("rel=\"noreferrer\"", link.Groups["attributes"].Value, StringComparison.Ordinal);

        // The address is the one the READMEs name, reached through a constant rather than typed out twice:
        // a dependency's address is a fact about the dependency, not interface text a translator rewrites.
        var href = Regex.Match(
            link.Groups["attributes"].Value,
            @"href=""@(?<name>[A-Za-z_][A-Za-z0-9_]*)""");
        Assert.True(href.Success, "The link's address should come from a constant.");
        var declared = Regex.Match(
            layout,
            @"private const string " + href.Groups["name"].Value + @" = ""(?<url>https?://[^""]+)"";");
        Assert.True(declared.Success, "The address should stay a declared constant.");
        foreach (var readme in new[] { "README.md", "README.ru.md" })
        {
            var documented = Regex.Match(
                File.ReadAllText(Path.Combine(root, readme)),
                @"\[Flare\.Blazor\]\((?<url>[^)]+)\)");
            Assert.True(documented.Success, $"{readme} should name the library's repository.");
            Assert.Equal(declared.Groups["url"].Value, documented.Groups["url"].Value);
        }

        // The version comes from the package's informational version, and the caption is what the rail
        // prints. The assembly identity is where it used to be read - the Flare packages carry 0.0.0.0
        // there, which is how "v0.0.0" reached the screen.
        Assert.DoesNotContain("GetName().Version", layout, StringComparison.Ordinal);
        Assert.Contains("AssemblyInformationalVersionAttribute", layout, StringComparison.Ordinal);
        Assert.Contains("@FlareVersionCaption", layout, StringComparison.Ordinal);

        // And the caption follows the package Aiko.Pwa.csproj actually references, so a number written
        // into the code (or into this test) cannot outlive the package it describes.
        var package = Regex.Match(
            File.ReadAllText(Path.Combine(root, "src", "Aiko.Pwa", "Aiko.Pwa.csproj")),
            @"<PackageReference[^>]*Include=""Flare\.Blazor""[^>]*>");
        Assert.True(package.Success, "Aiko.Pwa.csproj should reference the Flare.Blazor package.");
        var referenced = Regex.Match(package.Value, @"Version=""(?<version>[^""]+)""");
        Assert.True(referenced.Success, "The Flare.Blazor reference should state the version it pins.");
        Assert.Equal("v" + referenced.Groups["version"].Value, MainLayout.FlareVersionCaption);
        Assert.Equal(referenced.Groups["version"].Value, MainLayout.VersionOf(typeof(FlareText).Assembly));

        // A version of nothing but zeros counts as unstated, so the caption's old text is gone from the
        // client for good rather than merely overwritten in the one place that printed it.
        var offenders = new List<string>();
        foreach (var extension in new[] { "*.cs", "*.razor" })
        {
            foreach (var file in Directory.EnumerateFiles(
                         Path.Combine(root, "src", "Aiko.Pwa"), extension, SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                    file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                if (File.ReadAllText(file).Contains("v0.0.0", StringComparison.Ordinal))
                {
                    offenders.Add(file);
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "A zero is what an assembly says when it says nothing, and it should read as a dash:" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void The_card_tile_carries_the_backlog_slice_of_the_cards_it_counts()
    {
        var root = FindRepositoryRoot();
        var view = File.ReadAllText(
            Path.Combine(root, "src", "Aiko.Pwa", "Pages", "BoardView.razor"));

        // The strip stays the four metrics the design draws: the backlog is a reading of the card count, so
        // it rides on that tile as a line of its own instead of becoming a fifth number beside it.
        var tiles = Regex.Match(
            view,
            @"private IReadOnlyList<\(string Label, string Value, bool Warn, string\? Slice\)> SummaryTiles(?<body>.*?\n    })",
            RegexOptions.Singleline);
        Assert.True(tiles.Success, "SummaryTiles should stay the one place the strip is described.");
        var body = tiles.Groups["body"].Value;
        Assert.Equal(4, Regex.Matches(body, @"Loc\.Get\(").Count);
        Assert.Contains("\"BacklogSlice\"", body, StringComparison.Ordinal);

        // Its number comes from the shared counting rule, over the very set the count beside it counts: a
        // second definition of "in the backlog" is how two numbers for one thing appear.
        var slice = Regex.Match(body, @"BoardMetrics\.BacklogCount\((?<cards>[A-Za-z_][A-Za-z0-9_]*)\)");
        Assert.True(slice.Success, "The slice should come from BoardMetrics.BacklogCount.");
        Assert.Contains(
            $"DisplayFormat.Number({slice.Groups["cards"].Value}.Count)",
            body,
            StringComparison.Ordinal);
        Assert.DoesNotContain("BacklogStageId", view, StringComparison.Ordinal);

        // ... and it is drawn under the tile, in the strip's own muted mono tone.
        Assert.Contains("tile.Slice", view, StringComparison.Ordinal);
    }

    [Fact]
    public void The_backlog_slice_counts_what_the_rail_counts_and_follows_a_filtered_set()
    {
        var cards = new[]
        {
            CardIn("TASK-1", "Task", WorkflowDefinition.BacklogStageId),
            CardIn("TASK-2", "Task", "implementation"),
            CardIn("STORY-1", "Story", WorkflowDefinition.BacklogStageId)
        };

        // With no type filter the board shows every card, so the slice reports what the rail's backlog tag
        // reports ...
        Assert.Equal(2, BoardMetrics.BacklogCount(cards));

        // ... and with a type picked it reports the backlog of the cards the tile above it counts.
        var tasks = cards
            .Where(card => StringComparer.OrdinalIgnoreCase.Equals(card.Kind, "Task"))
            .ToArray();
        Assert.Equal(1, BoardMetrics.BacklogCount(tasks));

        // Both numbers are the one rule, so the rail's project-wide count is the sum of the slices over any
        // partition of the cards - which is what keeps two numbers for one thing from drifting apart.
        Assert.Equal(
            BoardMetrics.BacklogCount(cards),
            BoardMetrics.BacklogCount(tasks) +
            BoardMetrics.BacklogCount(cards.Where(card => !StringComparer.OrdinalIgnoreCase.Equals(card.Kind, "Task"))));
    }

    /// <summary>A card standing in one stage, for the counts that read cards and nothing else.</summary>
    private static Card CardIn(string id, string kind, string stageId) => new(
        new CardReference("p1", id),
        kind,
        id,
        kind.ToLowerInvariant(),
        stageId,
        Revision: 1,
        OwnPriority: 0m,
        DeclaredScopeFiles: [],
        ActualChangedFiles: [],
        Metadata: new Dictionary<string, string>(StringComparer.Ordinal));

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
