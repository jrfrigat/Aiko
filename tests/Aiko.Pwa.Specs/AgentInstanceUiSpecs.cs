using Aiko.Pwa.Services;
using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// The agent-instance screens: the settings page edits display name and environment of every configured
/// instance, and the card's command picker offers those instances and asks the daemon about that instance.
/// </summary>
public sealed class AgentInstanceUiSpecs
{
    [Fact]
    public void The_environment_editor_keeps_the_mask_and_drops_blank_keys()
    {
        var editor = AgentEnvironmentEditor.Load(new Dictionary<string, string>
        {
            ["ANTHROPIC_AUTH_TOKEN"] = "***",
            ["ANTHROPIC_MODEL"] = "deepseek-chat"
        });

        Assert.Equal(2, editor.Entries.Count);
        Assert.Equal("***", editor.Entries[0].Value);

        editor.Entries[0].Value = "new-secret";
        var added = editor.Add();
        added.Key = "EXTRA";
        added.Value = "1";
        var blank = editor.Add();
        blank.Key = " ";

        var environment = editor.ToEnvironment()!;

        Assert.Equal("new-secret", environment["ANTHROPIC_AUTH_TOKEN"]);
        Assert.Equal("deepseek-chat", environment["ANTHROPIC_MODEL"]);
        Assert.Equal("1", environment["EXTRA"]);
        Assert.DoesNotContain(
            environment.Keys,
            key => string.IsNullOrWhiteSpace(key));

        editor.Remove(blank);
        editor.Remove(editor.Entries[^1]);
        Assert.Equal(2, editor.Entries.Count);
        Assert.Null(AgentEnvironmentEditor.Load(null).ToEnvironment());
    }

    [Fact]
    public void The_settings_page_edits_instances_and_their_environment()
    {
        var text = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "Aiko.Pwa", "Pages", "ProjectSettingsPage.razor"));

        Assert.Contains("State.AgentConfigs", text, StringComparison.Ordinal);
        Assert.Contains("DisplayName", text, StringComparison.Ordinal);
        Assert.Contains("AgentEnvironmentEditor.Load", text, StringComparison.Ordinal);
        Assert.Contains("AgentEnvironmentHint", text, StringComparison.Ordinal);
        Assert.Contains("AgentInstanceAdd", text, StringComparison.Ordinal);
        Assert.Contains("AddInstanceAsync", text, StringComparison.Ordinal);
        Assert.Contains("SaveAgentConfigAsync", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_card_command_picker_offers_instances_and_asks_about_one()
    {
        var text = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "Aiko.Pwa", "Pages", "CardInspector.razor"));

        Assert.Contains("State.AgentConfigs.Count > 0", text, StringComparison.Ordinal);
        Assert.Contains("RequestedAgentId: _commandAgent", text, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestedAgentAdapterId: _commandAgent", text, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(AgentInstanceUiSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
