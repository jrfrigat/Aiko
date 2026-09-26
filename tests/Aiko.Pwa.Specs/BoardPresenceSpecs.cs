using Aiko.Pwa.Services;
using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// Guards the rule that keeps a hidden tab out of the board's reads, and keeps the plate off the screen for a
/// failure that happened while the person was looking somewhere else.
/// </summary>
/// <remarks>
/// The bug this pins: the client read the board exactly as often in a minimized window as in a visible one.
/// A hidden tab is throttled by the browser, so that read was answered late while the client's own hundred
/// second deadline kept running - and the shell put up "Could not update the board" with
/// "net_http_request_timedout, 100" under it while the daemon was answering. The same plate then outlived the
/// failure, because a reconnect took nothing away.
/// </remarks>
public sealed class BoardPresenceSpecs
{
    [Fact]
    public void A_tab_starts_on_screen_and_may_read_the_board()
    {
        var presence = new BoardPresence();

        // Only the browser can say a tab is hidden, so the state before it says anything is "on screen": that
        // is the behaviour the client had before it could ask, and it is the safe default.
        Assert.True(presence.Visible);
        Assert.True(presence.CanRead);
    }

    [Fact]
    public void A_hidden_tab_does_not_read_the_board()
    {
        var presence = new BoardPresence();

        Assert.True(presence.Apply(visible: false));
        Assert.False(presence.CanRead);

        // A read is allowed again the moment the tab is on screen again.
        Assert.True(presence.Apply(visible: true));
        Assert.True(presence.CanRead);
    }

    [Fact]
    public void A_read_in_flight_when_the_tab_left_may_not_report_its_failure()
    {
        var presence = new BoardPresence();
        var started = presence.Epoch;

        // Minimizing ends what was in flight: that answer belongs to a tab nobody was looking at, and a
        // deadline that passed over it is not the person's news.
        Assert.True(presence.Apply(visible: false));
        Assert.False(presence.IsCurrent(started));

        // The read the return makes is the one that gets to speak.
        Assert.True(presence.Apply(visible: true));
        Assert.True(presence.IsCurrent(presence.Epoch));
        Assert.False(presence.IsCurrent(started));
    }

    [Fact]
    public void A_read_started_while_the_tab_is_on_screen_still_speaks_for_it()
    {
        var presence = new BoardPresence();
        var started = presence.Epoch;

        // Nothing moved, so a failure of this read is real and must reach the screen.
        Assert.True(presence.IsCurrent(started));
    }

    [Fact]
    public void A_report_that_changes_nothing_is_not_a_reason_to_read_again()
    {
        var presence = new BoardPresence();

        // The state read when the subscription is made is the first report, and this tab is already visible.
        Assert.False(presence.Apply(visible: true));

        Assert.True(presence.Apply(visible: false));
        Assert.False(presence.Apply(visible: false));

        Assert.True(presence.Apply(visible: true));
        Assert.False(presence.Apply(visible: true));
    }

    [Fact]
    public void A_reconnect_ends_what_is_in_flight_without_a_change_of_visibility()
    {
        var presence = new BoardPresence();
        var started = presence.Epoch;

        presence.Abandon();

        // The link came back but the tab never moved: it is still on screen, still reads, and the read that was
        // waiting on the dead link says nothing any more.
        Assert.True(presence.Visible);
        Assert.True(presence.CanRead);
        Assert.False(presence.IsCurrent(started));
    }

    [Fact]
    public void The_client_asks_the_browser_whether_the_tab_is_on_screen()
    {
        var js = Text("src", "Aiko.Pwa", "wwwroot", "js", "aiko-events.js");
        Assert.Contains("window.aikoVisibility", js, StringComparison.Ordinal);
        Assert.Contains("document.addEventListener('visibilitychange', report)", js, StringComparison.Ordinal);
        Assert.Contains("'OnVisibilityChanged'", js, StringComparison.Ordinal);

        var state = Text("src", "Aiko.Pwa", "Services", "WorkspaceState.cs");
        Assert.Contains("\"aikoVisibility.subscribe\"", state, StringComparison.Ordinal);
        Assert.Contains("public async Task OnVisibilityChanged(bool visible)", state, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hidden_tab_starts_no_read_and_an_abandoned_one_never_reports()
    {
        var state = Text("src", "Aiko.Pwa", "Services", "WorkspaceState.cs");

        // The gate on the background read, and the epoch test that keeps an abandoned attempt quiet.
        Assert.Contains("if (!_presence.CanRead)", state, StringComparison.Ordinal);
        Assert.Contains("if (_presence.IsCurrent(epoch))", state, StringComparison.Ordinal);
    }

    [Fact]
    public void A_returning_link_takes_the_plate_away_and_reads_the_board()
    {
        var state = Text("src", "Aiko.Pwa", "Services", "WorkspaceState.cs");
        var reconnect = Between(
            state,
            "private async Task OnEventConnectionChangedAsync",
            "private async Task RefreshSystemAsync");

        Assert.Contains("_presence.Abandon();", reconnect, StringComparison.Ordinal);
        Assert.Contains("Error = null;", reconnect, StringComparison.Ordinal);
        Assert.Contains("await LoadBoardAsync();", reconnect, StringComparison.Ordinal);
    }

    [Fact]
    public void The_runtimes_deadline_is_told_in_words_a_person_can_act_on()
    {
        var failure = Text("src", "Aiko.Pwa", "Services", "FailureText.cs");
        Assert.Contains("net_http_request_timedout", failure, StringComparison.Ordinal);
        Assert.Contains("Loc.Get(\"DaemonTimeoutHint\")", failure, StringComparison.Ordinal);

        // Both resource sets carry the key: the neutral set is what every untranslated language reads, and the
        // pairing of the two is what LocalizationSpecs pins.
        foreach (var name in new[] { "Loc.resx", "Loc.ru.resx" })
        {
            var resources = Text("src", "Aiko.Pwa", "Resources", name);
            Assert.Contains("<data name=\"DaemonTimeoutHint\"", resources, StringComparison.Ordinal);
        }
    }

    /// <summary>The text between two markers, which is how one method is told from the next.</summary>
    private static string Between(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"'{start}' is not in the file.");
        var to = text.IndexOf(end, from, StringComparison.Ordinal);
        Assert.True(to > from, $"'{end}' does not follow '{start}'.");
        return text[from..to];
    }

    /// <summary>Reads a file of the repository, given its path below the root.</summary>
    private static string Text(params string[] segments) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. segments]));

    /// <summary>Walks up from this assembly to the solution file, as the other specs do.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(BoardPresenceSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
