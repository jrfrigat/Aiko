using Aiko.Domain.Workflow;
using Aiko.Pwa.Services;
using Flare.Components;
using Flare.Icons;
using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// A stage is shown with the icon and the colour its settings give it, on the card page and in "active runs"
/// alike, and both read the one translation the Workflow picker already uses, so what is set is what is seen.
/// </summary>
public sealed class StageAppearanceSpecs
{
    private static readonly WorkflowDefinition Task = new(
        "task",
        "Task",
        [
            Stage("backlog", "Backlog"),
            Stage("in-progress", "In progress", icon: "code", color: "warning")
        ],
        Revision: 1);

    [Fact]
    public void A_stage_is_found_in_its_own_workflow_and_nowhere_else()
    {
        Assert.Equal("In progress", StageAppearance.Find([Task], "task", "in-progress")?.Title);
        Assert.Null(StageAppearance.Find([Task], "epic", "in-progress"));
        Assert.Null(StageAppearance.Find([Task], "task", "gone"));
        Assert.Null(StageAppearance.Find(null, "task", "in-progress"));
    }

    [Fact]
    public void A_stage_is_named_by_its_title_and_by_its_id_only_when_the_workflow_no_longer_knows_it()
    {
        Assert.Equal("In progress", StageAppearance.Caption(Task.Stages[1], "in-progress"));
        Assert.Equal("gone", StageAppearance.Caption(null, "gone"));
    }

    [Fact]
    public void The_icon_and_colour_are_the_ones_the_settings_name()
    {
        var stage = Task.Stages[1];

        Assert.Same(FlareIcons.Code, StageAppearance.Icon(stage));
        Assert.Contains(FlareColor.Warning.CssValue!, StageAppearance.TagStyle(stage), StringComparison.Ordinal);
    }

    [Fact]
    public void A_stage_that_names_nothing_keeps_todays_look()
    {
        // No choice and a choice this client does not know both draw no icon and no tint, rather than a
        // default invented by position - and neither throws.
        var plain = Task.Stages[0];
        var unknown = Stage("odd", "Odd", icon: "no-such-icon", color: "no-such-colour");

        Assert.Null(StageAppearance.Icon(plain));
        Assert.Null(StageAppearance.TagStyle(plain));
        Assert.Null(StageAppearance.Icon(unknown));
        Assert.Null(StageAppearance.TagStyle(unknown));
        Assert.Null(StageAppearance.Icon(null));
        Assert.Null(StageAppearance.TagStyle(null));
    }

    [Theory]
    [InlineData("src", "Aiko.Pwa", "Pages", "CardPage.razor")]
    [InlineData("src", "Aiko.Pwa", "Pages", "CardInspector.razor")]
    [InlineData("src", "Aiko.Pwa", "Pages", "BoardView.razor")]
    public void Each_screen_that_names_the_stage_draws_it_through_the_shared_helper(params string[] path)
    {
        var markup = Read(path);

        Assert.Contains("StageAppearance.Icon(", markup, StringComparison.Ordinal);
        Assert.Contains("StageAppearance.TagStyle(", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Active_runs_name_the_stage_by_its_title_rather_than_its_id()
    {
        var markup = Read("src", "Aiko.Pwa", "Pages", "BoardView.razor");

        Assert.DoesNotContain("{row.StageId} ·", markup, StringComparison.Ordinal);
        Assert.Contains("StageAppearance.Caption(", markup, StringComparison.Ordinal);
    }

    private static StageDefinition Stage(string id, string title, string? icon = null, string? color = null) =>
        new(id, title, 1, string.Empty, [], null, [], new Dictionary<string, ActionPolicy>(), Icon: icon, Color: color);

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. segments])).Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>Walks up from this assembly to the solution file, as the other specs do.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(typeof(StageAppearanceSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
