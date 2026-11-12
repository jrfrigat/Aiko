using System.Text.RegularExpressions;
using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// The agent documentation covers what the instance model actually does - configuration, environment and
/// secrets, routing, history, the API and the UI - and both languages say the same thing.
/// </summary>
public sealed class AgentInstanceDocsSpecs
{
    [Theory]
    [InlineData(
        "docs/en/agent-integration.md",
        new[]
        {
            "Adapter and instance",
            "claude-code-deepseek",
            "\"environment\"",
            "***",
            "agentInstanceId",
            "history"
        })]
    [InlineData(
        "docs/ru/agent-integration.md",
        new[]
        {
            "Адаптер и экземпляр",
            "claude-code-deepseek",
            "environment",
            "***",
            "agentInstanceId",
            "историю"
        })]
    public void The_agent_documentation_describes_instances_and_secrets(
        string path,
        string[] markers)
    {
        var text = File.ReadAllText(Path.Combine(FindRepositoryRoot(), path));

        foreach (var marker in markers)
        {
            Assert.Contains(marker, text, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("README.md", "One adapter can run as several named instances")]
    [InlineData("README.ru.md", "Один адаптер может работать как несколько именованных экземпляров")]
    public void The_readme_names_configured_instances(string readme, string sentence)
    {
        var text = Regex.Replace(
            File.ReadAllText(Path.Combine(FindRepositoryRoot(), readme)),
            @"\s+",
            " ");

        Assert.Contains(sentence, text, StringComparison.Ordinal);
        Assert.Contains("docs/", text, StringComparison.Ordinal);
        Assert.Contains("agent-integration.md", text, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(AgentInstanceDocsSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
