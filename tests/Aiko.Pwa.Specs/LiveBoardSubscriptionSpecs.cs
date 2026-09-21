using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// Guards the live-update wiring of the screens that draw the open project's board.
/// </summary>
/// <remarks>
/// The bug this pins: an SSE event reloads the board in <c>WorkspaceState</c> and raises <c>Changed</c>, but a
/// page that reads <c>State.Board</c> in its markup and never subscribes keeps rendering the snapshot it was
/// built with. The board and the card page then stay stale until a manual reload while the shell's badge says
/// "SSE LIVE" - which is exactly how TASK-7 was closed as done without the live update working.
/// </remarks>
public sealed class LiveBoardSubscriptionSpecs
{
    [Fact]
    public void A_page_that_draws_the_live_board_re_renders_when_the_workspace_changes()
    {
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(FindRepositoryRoot(), "src", "Aiko.Pwa", "Pages"),
                     "*.razor",
                     SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(file);

            // Only a reference in the markup is a drawn snapshot of the board. One inside the code block may be
            // a one-off default (a template id, a form's starting type) that no live change has to repaint.
            var codeIndex = text.IndexOf("@code", StringComparison.Ordinal);
            var markup = codeIndex < 0 ? text : text[..codeIndex];
            if (!markup.Contains("State.Board", StringComparison.Ordinal))
            {
                continue;
            }

            if (!text.Contains("State.Changed += OnStateChanged;", StringComparison.Ordinal) ||
                !text.Contains("State.Changed -= OnStateChanged;", StringComparison.Ordinal) ||
                !text.Contains("@implements IAsyncDisposable", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetFileName(file));
            }
        }

        Assert.True(
            offenders.Count == 0,
            "A page that draws State.Board must subscribe to State.Changed in OnInitialized and unsubscribe in "
            + "DisposeAsync, or it will not re-render after a live update. Missing on:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void The_board_and_the_card_page_are_covered()
    {
        // The two screens the owner reported stale: whatever else changes, these must keep following the board.
        var pages = Path.Combine(FindRepositoryRoot(), "src", "Aiko.Pwa", "Pages");
        foreach (var page in new[] { "BoardView.razor", "CardPage.razor" })
        {
            var text = File.ReadAllText(Path.Combine(pages, page));
            Assert.Contains("State.Changed += OnStateChanged;", text, StringComparison.Ordinal);
            Assert.Contains("State.Changed -= OnStateChanged;", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void One_browser_holds_one_stream_per_project()
    {
        // The defect behind this: every tab held its own EventSource, and the daemon serves plain HTTP - so the
        // transport is HTTP/1.1, which allows about six connections to the origin. Five tabs spent five of
        // them, the sixth had none left, and every request of every tab queued behind streams that only ended
        // with the tab. A shared worker is what makes the count stop depending on the number of tabs.
        var worker = Text("src", "Aiko.Pwa", "wwwroot", "js", "aiko-events-worker.js");

        Assert.Contains("self.onconnect", worker, StringComparison.Ordinal);
        Assert.Equal(1, Count(worker, "new EventSource("));

        // One channel per stream address, holding the ports that listen to it ...
        Assert.Contains("channels.get(url)", worker, StringComparison.Ordinal);
        Assert.Contains("channel.ports.add(port)", worker, StringComparison.Ordinal);
        Assert.Contains("channel.ports.delete(port)", worker, StringComparison.Ordinal);

        // ... and the stream ends with the last tab that wanted it, so a browser with no tab open holds no
        // connection at all.
        Assert.Contains("channel.source.close()", worker, StringComparison.Ordinal);
        Assert.Contains("channels.delete(channel.url)", worker, StringComparison.Ordinal);

        // A tab that arrives while the stream is already up is told so, instead of waiting for the next event
        // to learn that it is live.
        Assert.Contains("port.postMessage({ type: 'open' })", worker, StringComparison.Ordinal);
    }

    [Fact]
    public void The_page_prefers_the_worker_and_keeps_the_per_tab_stream_for_when_it_is_refused()
    {
        var js = Text("src", "Aiko.Pwa", "wwwroot", "js", "aiko-events.js");

        // The page must ask for the worker by the path the file actually lives at, or every tab silently falls
        // back to the stream that caused the freeze.
        Assert.Contains("new SharedWorker('js/aiko-events-worker.js'", js, StringComparison.Ordinal);

        // A private window or an engine without SharedWorker is not a failure: the per-tab stream carries on.
        Assert.Contains("typeof SharedWorker !== 'function'", js, StringComparison.Ordinal);
        Assert.Contains("connectDirectly(url, dotnet)", js, StringComparison.Ordinal);
        Assert.Contains("new EventSource(url)", js, StringComparison.Ordinal);
    }

    [Fact]
    public void The_failure_probe_runs_once_for_every_tab()
    {
        // EventSource reconnects by itself and never says why, so the reason is fetched - and that fetch is a
        // second connection on the same address, held until the timer aborts it. Asking per tab is what turned
        // one dropped stream into one extra connection per tab; the worker asks once and shares the answer.
        var worker = Text("src", "Aiko.Pwa", "wwwroot", "js", "aiko-events-worker.js");
        Assert.Contains("'Accept': 'text/event-stream'", worker, StringComparison.Ordinal);
        Assert.Contains("channel.probing", worker, StringComparison.Ordinal);
        Assert.Contains("broadcast(channel, { type: 'error', reason: channel.reason })", worker, StringComparison.Ordinal);

        // The tab that went through the worker subscribes and waits: the probe is not its business any more, so
        // nothing on that path opens a connection of its own.
        var js = Text("src", "Aiko.Pwa", "wwwroot", "js", "aiko-events.js");
        var shared = Between(js, "function connectThroughWorker", "function connectDirectly");
        Assert.Contains("port.postMessage({ type: 'subscribe', url: url })", shared, StringComparison.Ordinal);
        Assert.DoesNotContain("fetch(", shared, StringComparison.Ordinal);

        // The fallback has no worker to ask, so there it still asks for itself.
        var direct = js[js.IndexOf("function connectDirectly", StringComparison.Ordinal)..];
        Assert.Contains("fetch(url", direct, StringComparison.Ordinal);
    }

    /// <summary>The text between two markers, which is how one function of a script is told from the next.</summary>
    private static string Between(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"'{start}' is not in the file.");
        var to = text.IndexOf(end, from, StringComparison.Ordinal);
        Assert.True(to > from, $"'{end}' does not follow '{start}'.");
        return text[from..to];
    }

    [Fact]
    public void The_interop_surface_the_client_calls_is_unchanged()
    {
        // ProjectEventClient holds on to whatever connect returned and hands it back to close, so both sides of
        // that handshake have to keep their names and their shape - the transport moved, not the surface.
        var js = Text("src", "Aiko.Pwa", "wwwroot", "js", "aiko-events.js");
        Assert.Contains("connect: function (url, dotnet)", js, StringComparison.Ordinal);
        Assert.Contains("close: function (handle)", js, StringComparison.Ordinal);

        var client = Text("src", "Aiko.Pwa", "Services", "ProjectEventClient.cs");
        Assert.Contains("\"aikoEvents.connect\"", client, StringComparison.Ordinal);
        Assert.Contains("\"aikoEvents.close\"", client, StringComparison.Ordinal);
    }

    /// <summary>Reads a file of the repository, given its path below the root.</summary>
    private static string Text(params string[] segments) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. segments]));

    /// <summary>How many times <paramref name="needle"/> occurs in <paramref name="text"/>.</summary>
    private static int Count(string text, string needle)
    {
        var count = 0;
        var index = text.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }

    /// <summary>Walks up from this assembly to the solution file, as the other specs do.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(LiveBoardSubscriptionSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
