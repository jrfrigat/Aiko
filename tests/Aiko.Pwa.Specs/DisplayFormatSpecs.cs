using Aiko.Application.Agents;
using Aiko.Pwa.Services;
using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// Guards the small formatters the shell and the dashboard share.
/// </summary>
public sealed class DisplayFormatSpecs
{
    [Fact]
    public void A_priority_is_printed_on_the_scale_the_board_ranks_by()
    {
        // The formula's score is a share of the maximum, and printing it as a fraction made the board say
        // "0,47" beside "1" - two figures nobody compares. The scale is 0..100 instead.
        Assert.Equal("72", DisplayFormat.Priority(0.72m));
        Assert.Equal("38", DisplayFormat.Priority(0.376m));
        Assert.Equal("100", DisplayFormat.Priority(1m));
        // The size coefficient can lift a card past the maximum, and the figure says so rather than hiding it.
        Assert.Equal("115", DisplayFormat.Priority(1.15m));
        Assert.Equal("0", DisplayFormat.Priority(0m));
        // Rounded away from zero: two cards a thousandth apart still read as different figures.
        Assert.Equal("65", DisplayFormat.Priority(0.645m));
    }

    [Fact]
    public void Agent_state_distinguishes_found_from_not_found()
    {
        // Nothing was found on PATH: this is the only case that may say "not found".
        Assert.Equal("not found", DisplayFormat.AgentState(null, "not found"));

        // Found, version unknown - which is every adapter today, because discovery scans PATH for the
        // executable and never runs it. This line used to print "not found" for a present agent.
        Assert.Equal(
            "claude.cmd",
            DisplayFormat.AgentState(
                new AgentInstallation(
                    "claude-code:C:/npm/claude.cmd",
                    "claude-code",
                    @"C:\Users\me\AppData\Roaming\npm\claude.cmd",
                    null),
                "not found"));

        // A reported version wins over the path, and the path is split on both separators because the
        // client runs in a browser.
        Assert.Equal(
            "1.2.3",
            DisplayFormat.AgentState(
                new AgentInstallation("id", "codex", "/usr/local/bin/codex", "1.2.3"),
                "not found"));
        Assert.Equal(
            "codex",
            DisplayFormat.AgentState(
                new AgentInstallation("id", "codex", "/usr/local/bin/codex", null),
                "not found"));

        // A found installation without a usable path cannot claim to be missing either.
        Assert.Equal(
            "not found",
            DisplayFormat.AgentState(new AgentInstallation("id", "codex", string.Empty, null), "not found"));
    }

    [Fact]
    public void A_caption_with_a_count_always_prints_the_count()
    {
        // The tab captions say how much sits behind them, and a zero is a fact about the card: a caption that
        // dropped it read as "not loaded yet", which is what the discussion tab used to do.
        Assert.Equal("Artifacts (0)", DisplayFormat.Counted("Artifacts", 0));
        Assert.Equal("Runs (2)", DisplayFormat.Counted("Runs", 2));

        // The shape carries no language: parentheses and digits read the same, so a translated title needs no
        // separate format key, and the digits stay invariant whatever the browser's locale is.
        Assert.Equal("Обсуждение (12)", DisplayFormat.Counted("Обсуждение", 12));
        Assert.Equal("Название (1000)", DisplayFormat.Counted("Название", 1000));
    }

    [Fact]
    public void Project_tag_shows_the_last_two_segments()
    {
        Assert.Equal("FrigaT/StitchFlow", DisplayFormat.ProjectTag(@"C:\Job\Projects\FrigaT\StitchFlow"));
        Assert.Equal("FrigaT/StitchFlow", DisplayFormat.ProjectTag("/home/me/FrigaT/StitchFlow"));
        Assert.Equal("single", DisplayFormat.ProjectTag("single"));
    }
    [Fact]
    public void Agent_presence_separates_detected_from_connected()
    {
        // Not on PATH: no action to offer.
        Assert.Equal(DisplayFormat.AgentPresence.NotDetected, DisplayFormat.Classify(Option([])));

        // On PATH with none of Aiko's files written: installed, but not connected.
        Assert.Equal(
            DisplayFormat.AgentPresence.NotConnected,
            DisplayFormat.Classify(Option(Installations(), new AgentUserScope(0, 9))));

        // Some of them: a partial, outdated connection - the state an upgrade leaves behind.
        Assert.Equal(
            DisplayFormat.AgentPresence.PartiallyConnected,
            DisplayFormat.Classify(Option(Installations(), new AgentUserScope(3, 9))));

        // All of them: connected.
        Assert.Equal(
            DisplayFormat.AgentPresence.Connected,
            DisplayFormat.Classify(Option(Installations(), new AgentUserScope(9, 9))));

        // An adapter without a user scope can be detected but never "connected".
        Assert.Equal(
            DisplayFormat.AgentPresence.NotConnected,
            DisplayFormat.Classify(Option(Installations(), new AgentUserScope(0, 0))));
    }

    private static AgentAdapterOption Option(
        IReadOnlyList<AgentInstallation> installations,
        AgentUserScope? userScope = null) =>
        new("codex", "Codex", AgentCapabilities.McpStdio, installations, false, userScope);

    private static IReadOnlyList<AgentInstallation> Installations() =>
        [new AgentInstallation("codex:/bin/codex", "codex", "/bin/codex", null)];


}
