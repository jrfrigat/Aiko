using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// A page being edited is not thrown away behind the person's back. Two things used to do exactly that: the
/// shell replaced the page with its spinner on every refresh, which destroys the component and its unsaved
/// fields, and the settings editor read its settings again on every render its parent made - which, with the
/// live stream re-rendering the page on each event, undid edits while an agent was working.
/// </summary>
/// <remarks>
/// Read from the markup, as the other screen specs do, until the PWA has component tests (TASK-216).
/// </remarks>
public sealed class UnsavedInputSpecs
{
    [Fact]
    public void A_refresh_keeps_the_page_on_screen_and_only_the_first_load_shows_the_spinner_instead()
    {
        var layout = Read("src", "Aiko.Pwa", "Layout", "MainLayout.razor");
        var state = Read("src", "Aiko.Pwa", "Services", "WorkspaceState.cs");

        // A refresh is its own state: the page stays and only the button shows that something is in flight.
        Assert.Contains("public bool Refreshing { get; private set; }", state, StringComparison.Ordinal);
        var reload = state[state.IndexOf("public async Task ReloadAsync()", StringComparison.Ordinal)..];
        reload = reload[..reload.IndexOf("\n    }", StringComparison.Ordinal)];
        Assert.Contains("Refreshing = true;", reload, StringComparison.Ordinal);
        Assert.DoesNotContain("Loading = true;", reload, StringComparison.Ordinal);

        Assert.Contains("Loading=\"@(State.Loading || State.Refreshing)\"", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void The_settings_editor_reads_its_settings_when_what_it_edits_changes_not_on_every_render()
    {
        var editor = Read("src", "Aiko.Pwa", "Pages", "SettingsEditor.razor");
        var parametersSet = editor[editor.IndexOf("protected override async Task OnParametersSetAsync()", StringComparison.Ordinal)..];
        parametersSet = parametersSet[..parametersSet.IndexOf("\n    }", StringComparison.Ordinal)];

        Assert.Contains("_loadedKey", parametersSet, StringComparison.Ordinal);
        Assert.Contains("return;", parametersSet, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. segments])).Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>Walks up from this assembly to the solution file, as the other specs do.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(UnsavedInputSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
