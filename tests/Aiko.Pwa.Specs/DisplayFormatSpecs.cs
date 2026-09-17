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
    public void Project_tag_shows_the_last_two_segments()
    {
        Assert.Equal("FrigaT/StitchFlow", DisplayFormat.ProjectTag(@"C:\Job\Projects\FrigaT\StitchFlow"));
        Assert.Equal("FrigaT/StitchFlow", DisplayFormat.ProjectTag("/home/me/FrigaT/StitchFlow"));
        Assert.Equal("single", DisplayFormat.ProjectTag("single"));
    }
}
