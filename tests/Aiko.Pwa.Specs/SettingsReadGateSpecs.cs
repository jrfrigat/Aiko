using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// The settings screen drew values it had not read. On a project switch the editor kept the form on screen
/// while its own read was in flight, so the defaults - or, when the component survived the switch, the level
/// read before - were painted and replaced a moment later, and the agents panel was drawn from the previous
/// project's connections until the second read came back. A screen owes one answer per level: either it has
/// read the level or it has not, and until it has it says nothing about it.
/// </summary>
/// <remarks>
/// Read from the markup, as the other screen specs do, until the PWA has component tests (TASK-216).
/// </remarks>
public sealed class SettingsReadGateSpecs
{
    [Fact]
    public void The_form_is_not_drawn_before_the_read_that_speaks_for_it_lands()
    {
        var editor = Read("src", "Aiko.Pwa", "Pages", "SettingsEditor.razor");

        // The gate is the whole fix: what the read is *for* and what it has *produced* are two different
        // things, and only the second one allows the form on screen.
        Assert.Contains("@if (_appliedFor != _loadedKey)", editor, StringComparison.Ordinal);

        // And the wait is answered with this screen's own shape - the two columns it really draws - rather
        // than with a placeholder of another shape that the values then replace.
        var skeleton = editor.IndexOf("<AikoSkeletonRegion", StringComparison.Ordinal);
        var form = editor.IndexOf("@ScopeCaption", StringComparison.Ordinal);
        Assert.True(skeleton >= 0, "SettingsEditor should draw its own shape while the level is being read.");
        Assert.True(form > skeleton, "The form should be drawn after the wait, not before it.");
    }

    [Fact]
    public void A_read_that_started_is_not_a_read_that_landed()
    {
        var editor = Read("src", "Aiko.Pwa", "Pages", "SettingsEditor.razor");

        // The key is remembered when the read *starts*: that is what keeps a parent's render from reading the
        // settings again and throwing unsaved edits away. It is not evidence that anything came back.
        var parametersSet = editor[editor.IndexOf("protected override async Task OnParametersSetAsync()", StringComparison.Ordinal)..];
        parametersSet = parametersSet[..parametersSet.IndexOf("\n    }", StringComparison.Ordinal)];
        Assert.Contains("_loadedKey = key;", parametersSet, StringComparison.Ordinal);
        Assert.DoesNotContain("_appliedFor", parametersSet, StringComparison.Ordinal);

        // Only the values being in says the level is on screen.
        var applyView = editor[editor.IndexOf("private void ApplyView(AppSettingsView? view)", StringComparison.Ordinal)..];
        applyView = applyView[..applyView.IndexOf("\n    }", StringComparison.Ordinal)];
        Assert.Contains("_appliedFor = _loadedKey;", applyView, StringComparison.Ordinal);
    }

    [Fact]
    public void A_read_that_fails_is_reported_over_an_empty_form_rather_than_another_levels_numbers()
    {
        var editor = Read("src", "Aiko.Pwa", "Pages", "SettingsEditor.razor");

        // A failure ends the wait too, and the screen owes the reason for it rather than a form that will
        // never come - but the values that come with the message are this level's own starting point.
        var load = editor[editor.IndexOf("private async Task LoadAsync()", StringComparison.Ordinal)..];
        load = load[..load.IndexOf("\n    }", StringComparison.Ordinal)];
        var failure = load[load.IndexOf("catch (Exception exception)", StringComparison.Ordinal)..];
        Assert.Contains("ClearApplied();", failure, StringComparison.Ordinal);
        Assert.Contains("_appliedFor = _loadedKey;", failure, StringComparison.Ordinal);

        var clear = editor[editor.IndexOf("private void ClearApplied()", StringComparison.Ordinal)..];
        clear = clear[..clear.IndexOf("\n    }", StringComparison.Ordinal)];
        Assert.Contains("_criteria = [];", clear, StringComparison.Ordinal);
        Assert.Contains("_sizes = [];", clear, StringComparison.Ordinal);
        Assert.Contains("_maxRuns = 1;", clear, StringComparison.Ordinal);
    }

    [Fact]
    public void Switching_project_clears_what_belonged_to_the_project_that_was_open()
    {
        var state = Read("src", "Aiko.Pwa", "Services", "WorkspaceState.cs");

        // The board and the effective settings were cleared on a switch; the project's agent connections were
        // not, and the settings screen draws its agents panel straight from them.
        var switchTo = state[state.IndexOf("SelectedProjectId = projectId;", StringComparison.Ordinal)..];
        switchTo = switchTo[..switchTo.IndexOf("Loading = true;", StringComparison.Ordinal)];
        Assert.Contains("Board = null;", switchTo, StringComparison.Ordinal);
        Assert.Contains("SettingsView = null;", switchTo, StringComparison.Ordinal);
        Assert.Contains("ProjectConnections = [];", switchTo, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. segments])).Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>Walks up from this assembly to the solution file, as the other specs do.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(SettingsReadGateSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
