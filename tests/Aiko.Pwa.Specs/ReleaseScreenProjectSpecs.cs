using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// The release screens stand at the project's own address and are reached from its rail, so opening one has to
/// open the project the way the screens beside it do. They were the only project screens that did not: the
/// rail said nothing about the project the person was plainly looking at, and an address naming a project
/// nobody has drew an empty release history - which reads as "nothing has been released yet" rather than as a
/// refusal.
/// </summary>
/// <remarks>
/// Read from the markup, as the other screen specs do, until the PWA has component tests (TASK-216).
/// </remarks>
public sealed class ReleaseScreenProjectSpecs
{
    private static readonly string[] Screens = ["ReleasePage.razor", "ReleaseRecordPage.razor"];

    public static IEnumerable<object[]> ReleaseScreens() =>
        Screens.Select(file => new object[] { file });

    [Theory]
    [MemberData(nameof(ReleaseScreens))]
    public void A_release_screen_opens_the_project_it_addresses(string file)
    {
        var page = Read("src", "Aiko.Pwa", "Pages", file);
        var parametersSet = page[page.IndexOf("protected override async Task OnParametersSetAsync()", StringComparison.Ordinal)..];
        parametersSet = parametersSet[..parametersSet.IndexOf("\n    }", StringComparison.Ordinal)];

        // Opened by the handle the address named - that is what the rail and the live stream are keyed on -
        // and opened before the screen reads what it shows.
        var opened = parametersSet.IndexOf("await State.EnsureProjectAsync(Handle);", StringComparison.Ordinal);
        var read = parametersSet.IndexOf("await ReloadAsync();", StringComparison.Ordinal);
        Assert.True(opened >= 0, $"{file} should open the project it is addressed for.");
        Assert.True(read > opened, $"{file} should open the project before reading its own content.");
    }

    [Theory]
    [MemberData(nameof(ReleaseScreens))]
    public void A_project_that_did_not_open_is_a_refusal_rather_than_an_empty_page(string file)
    {
        var page = Read("src", "Aiko.Pwa", "Pages", file);
        var reload = page[page.IndexOf("private async Task ReloadAsync()", StringComparison.Ordinal)..];
        reload = reload[..reload.IndexOf("\n    }", StringComparison.Ordinal)];

        // The refusal stands before the reads, so a page whose project failed gets the daemon's reason instead
        // of a read that would fail with the same answer - or worse, succeed with an empty list.
        var refusal = reload.IndexOf("if (State.Board is null && State.Error is { Length: > 0 }", StringComparison.Ordinal);
        Assert.True(refusal >= 0, $"{file} should answer a project that did not open before drawing anything.");
        Assert.True(
            reload.IndexOf("await Http.GetFromJsonAsync", StringComparison.Ordinal) > refusal,
            $"{file} should not read its content for a project that did not open.");
        Assert.Contains("_message = refusal;", reload, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. segments])).Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>Walks up from this assembly to the solution file, as the other specs do.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(ReleaseScreenProjectSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
