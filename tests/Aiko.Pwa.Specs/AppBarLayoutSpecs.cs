using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// The top bar says what the shell is and nothing else. The daemon's health has two other homes - the rail's
/// foot block and the daemon page - and the bar used to repeat all of it, which is what made a cockpit read
/// as telemetry (TASK-340).
/// </summary>
public sealed class AppBarLayoutSpecs
{
    [Fact]
    public void The_top_bar_carries_no_telemetry()
    {
        var layout = Read("src", "Aiko.Pwa", "Layout", "MainLayout.razor");

        // The runtime the daemon runs on, the endpoint it answers at and the round-trip it measured: three
        // facts about the machine, said where the machine is the subject (the daemon page) rather than here.
        Assert.DoesNotContain("system.Runtime", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("State.HostCaption", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("LatencyCaption", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("Loc.Get(\"SseLive\")", layout, StringComparison.Ordinal);

        // What someone has to act on stays: a stream that stopped, and a daemon serving a newer client. Both
        // say nothing while all is well.
        Assert.Contains("State.EventsStatusMessage", layout, StringComparison.Ordinal);
        Assert.Contains("@if (State.UpdateAvailable)", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void The_repository_mark_is_separated_from_the_actions_and_the_search_is_wider()
    {
        var layout = Read("src", "Aiko.Pwa", "Layout", "MainLayout.razor");

        // The mark belongs in the corner. It stood eight pixels after the reload button with a third of the
        // bar empty beyond it, because the brand's group was the one that stretched: the growth now belongs
        // to the action cluster, and the spacer is what puts the mark at its end.
        Assert.Contains("Class=\"aiko-appbar__row\"", layout, StringComparison.Ordinal);
        Assert.Contains("Class=\"aiko-appbar__actions\"", layout, StringComparison.Ordinal);
        Assert.Contains("<FlareSpacer />", layout, StringComparison.Ordinal);

        var spacer = layout.IndexOf("<FlareSpacer />", StringComparison.Ordinal);
        var mark = layout.IndexOf("Href=\"@GitHubUrl\"", StringComparison.Ordinal);
        Assert.True(spacer >= 0 && mark > spacer, "The spacer stands before the repository mark.");

        // And the field is no longer the design's 256-pixel slot: it is wider than it was, and still able to
        // give the width back in a narrow window rather than pushing the actions out of the bar.
        var css = Read("src", "Aiko.Pwa", "wwwroot", "css", "app.css");
        var rule = System.Text.RegularExpressions.Regex.Match(
            css,
            @"\.aiko-search\s*\{(?<body>[^}]*)\}",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        Assert.True(rule.Success, "The quick search needs a rule of its own.");
        Assert.Contains("width: 24rem", rule.Groups["body"].Value, StringComparison.Ordinal);
        Assert.Contains("flex: 0 1 auto", rule.Groups["body"].Value, StringComparison.Ordinal);

        var actions = System.Text.RegularExpressions.Regex.Match(
            css,
            @"\.aiko-appbar__actions\s*\{(?<body>[^}]*)\}",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        Assert.True(actions.Success, "The action cluster is what takes the rest of the row.");
        Assert.Contains("flex: 1 1 auto", actions.Groups["body"].Value, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. segments]))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(AppBarLayoutSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
