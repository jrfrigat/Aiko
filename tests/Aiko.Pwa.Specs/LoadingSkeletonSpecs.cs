using Aiko.Pwa.Services;
using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// Guards what a screen draws while it waits. The cockpit used to answer a slow project with a message
/// about a project it could not read, or with a spinner where the page should be, and neither says what
/// is coming. The wait and the failure are different answers, and every screen owes the first one.
/// </summary>
/// <remarks>
/// Read from the markup, as the other screen specs do, until the PWA has component tests (TASK-216). The
/// one rule that is not markup - which of the two answers applies - is pinned against the function that
/// decides it rather than against the property, which needs a live client to say anything.
/// </remarks>
public sealed class LoadingSkeletonSpecs
{
    /// <summary>The screens that stand on the project's snapshot, each of which owes a placeholder.</summary>
    private static readonly string[] BoardScreenFiles =
    [
        "CardPage.razor",
        "ProjectSettingsPage.razor",
        "WorkflowPage.razor",
        "BoardView.razor",
        "BacklogPage.razor",
        "StatsPage.razor",
        "NewCardPage.razor"
    ];

    /// <summary>
    /// The screens and blocks whose data is their own rather than the board, and which used a bare
    /// spinner or a message about something that had merely not arrived.
    /// </summary>
    private static readonly string[] SelfLoadingScreenFiles =
        ["Dashboard.razor", "TemplatesPage.razor", "AppSettingsPage.razor", "CardInspector.razor"];

    public static IEnumerable<object[]> BoardScreens() =>
        BoardScreenFiles.Select(file => new object[] { file });

    [Theory]
    [MemberData(nameof(BoardScreens))]
    public void A_screen_still_waiting_draws_its_own_shape_instead_of_the_unavailable_message(string file)
    {
        var page = Read("src", "Aiko.Pwa", "Pages", file);

        // The wait is answered first and by a skeleton, so the message about an unreadable board is what
        // is left for a read that actually failed.
        var skeleton = page.IndexOf("<AikoSkeletonRegion", StringComparison.Ordinal);
        var unavailable = page.IndexOf("Loc.Get(\"BoardUnavailable\")", StringComparison.Ordinal);
        Assert.True(skeleton >= 0, $"{file} should draw a skeleton while its project is being opened.");
        Assert.True(
            unavailable < 0 || skeleton < unavailable,
            $"{file} should answer the wait before the failure, not the other way round.");
        Assert.Contains("State.BoardPending", page, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(SelfLoadingScreens))]
    public void The_screens_whose_data_is_not_the_board_draw_a_shape_too(string file)
    {
        // The dashboard reads its own totals, the template list its own root, the template editor its own
        // template and the card's code tab its own patch, so none of them can ask the workspace whether a
        // board is on its way - and each used to answer with a bare spinner or with a message about
        // something that had merely not arrived yet.
        var page = Read("src", "Aiko.Pwa", "Pages", file);
        Assert.Contains("<AikoSkeletonRegion", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<FlareProgressCircular", page, StringComparison.Ordinal);

        // And the wait is answered before the message, as on the screens the board carries.
        var skeleton = page.IndexOf("<AikoSkeletonRegion", StringComparison.Ordinal);
        var unavailable = page.IndexOf("Loc.Get(\"WorkflowUnavailable\")", StringComparison.Ordinal);
        Assert.True(
            unavailable < 0 || skeleton < unavailable,
            $"{file} should answer the wait before the failure, not the other way round.");
    }

    public static IEnumerable<object[]> SelfLoadingScreens() =>
        SelfLoadingScreenFiles.Select(file => new object[] { file });

    [Fact]
    public void The_placeholder_is_the_librarys_and_the_shape_is_written_once()
    {
        var primitive = Read("src", "Aiko.Pwa", "Pages", "AikoSkeleton.razor");
        Assert.Contains("<FlareSkeleton", primitive, StringComparison.Ordinal);

        // One primitive under every screen: a page that reached for FlareSkeleton itself would be a second
        // description of a block, free to drift from this one.
        foreach (var file in BoardScreenFiles
                     .Concat(SelfLoadingScreenFiles)
                     .Concat(["AikoSkeletonPanel.razor"]))
        {
            var page = Read("src", "Aiko.Pwa", "Pages", file);
            Assert.DoesNotContain("<FlareSkeleton", page, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_wait_announces_itself_once()
    {
        var region = Read("src", "Aiko.Pwa", "Pages", "AikoSkeletonRegion.razor");

        // The blocks inside are decorative, so the announcement is the region's. The caption is the one the
        // shell's spinner used to carry, so a waiting screen still says the same words out loud.
        Assert.Contains("role=\"status\"", region, StringComparison.Ordinal);
        Assert.Contains("Label ?? Loc.Get(\"LoadingContext\")", region, StringComparison.Ordinal);

        // A region that stands for one block inside an already drawn page names its own wait, and keeps the
        // words that block used to show: the caption the card's code tab carried is still what is read out.
        Assert.Contains(
            "Label=\"@Loc.Get(\"LoadingCodeChanges\")\"",
            Read("src", "Aiko.Pwa", "Pages", "CardInspector.razor"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_shell_keeps_the_page_on_screen_while_it_loads()
    {
        var layout = Read("src", "Aiko.Pwa", "Layout", "MainLayout.razor");

        // The page is drawn whether or not a read is in flight: a shell that swapped it for a centred
        // spinner hid every screen's own loading shape, and the screens only draw one while it is there.
        Assert.Contains("<FlareProgressLinear", layout, StringComparison.Ordinal);
        Assert.Contains("id=\"aiko-main\"", layout, StringComparison.Ordinal);

        // The refresh button is the shell's other progress affordance and keeps its own loading state.
        Assert.Contains("Loading=\"@(State.Loading || State.Refreshing)\"", layout, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false, null, false, false, true)]   // The first load: the shell is still being read.
    [InlineData(true, true, "aiko", false, false, true)]   // A project switch: the read is in flight.
    [InlineData(true, true, "aiko", false, true, false)]   // A project whose read failed: this is a failure.
    [InlineData(false, true, "aiko", true, false, false)]  // The board arrived: nothing is waiting.
    [InlineData(false, true, null, false, false, false)]   // The dashboard: no project, so nothing is due.
    public void The_wait_and_the_failure_are_told_apart(
        bool loading,
        bool shellLoaded,
        string? selectedProjectId,
        bool hasBoard,
        bool hasError,
        bool expected)
    {
        Assert.Equal(
            expected,
            WorkspaceState.IsBoardPending(loading, shellLoaded, selectedProjectId, hasBoard, hasError));
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. segments]))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>Walks up from this assembly to the solution file, as the other specs do.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(LoadingSkeletonSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
