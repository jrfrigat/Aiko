using System.Text.Json;
using Aiko.Application.Contracts;
using Aiko.Infrastructure.Projects;
using Aiko.Infrastructure.Storage;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// Guards the base template the installer ships.
/// </summary>
/// <remarks>
/// Two things write the base template: the install script copies <c>assets/templates/default/template.json</c>
/// into the daemon's data directory, and the daemon writes the same document itself when nothing is shipped
/// (a source build). If those drift, an installation and a working copy would start new projects from
/// different defaults, and nothing else would notice - so the shipped file is pinned to the built-in one
/// here. Regenerate it by taking the file the daemon writes for an empty templates root.
/// </remarks>
public sealed class ShippedTemplateSpecs
{
    [Fact]
    public async Task The_shipped_base_template_is_the_one_the_daemon_writes()
    {
        var root = Path.Combine(Path.GetTempPath(), "Aiko.Specs", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AikoDataPaths(Path.Combine(root, "data", "aiko.db"));
            var store = new FileProjectTemplateStore(paths);
            var written = await store.EnsureDefaultAsync(CancellationToken.None);

            var writtenPath = Path.Combine(paths.TemplateDirectory(written.Id), "template.json");
            var shippedPath = Path.Combine(
                FindRepositoryRoot(),
                "assets",
                "templates",
                ProjectTemplate.DefaultId,
                "template.json");

            Assert.True(
                File.Exists(shippedPath),
                $"The installer's base template is missing: {shippedPath}");

            // Line endings are not the contract; the document is.
            static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

            Assert.Equal(
                Normalize(await File.ReadAllTextAsync(writtenPath)),
                Normalize(await File.ReadAllTextAsync(shippedPath)));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task The_base_template_ships_an_epic_pipeline_beside_stories_and_tasks()
    {
        var shippedPath = Path.Combine(
            FindRepositoryRoot(),
            "assets",
            "templates",
            ProjectTemplate.DefaultId,
            "template.json");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(shippedPath));
        var root = document.RootElement;

        // The version travels with the content: a project's manifest says which revision of the defaults it
        // was created from, so a template whose content changed must say so.
        Assert.Equal(ProjectTemplate.DefaultVersion, root.GetProperty("version").GetInt32());

        var workflows = root.GetProperty("workflows").EnumerateArray().ToArray();
        var epic = Assert.Single(workflows, workflow =>
            string.Equals(workflow.GetProperty("id").GetString(), "epic", StringComparison.Ordinal));
        Assert.Contains(workflows, workflow =>
            string.Equals(workflow.GetProperty("id").GetString(), "story", StringComparison.Ordinal));
        Assert.Contains(workflows, workflow =>
            string.Equals(workflow.GetProperty("id").GetString(), "task", StringComparison.Ordinal));

        // A type an agent reads before it works a card needs a stated purpose: "test" is not one, and neither is
        // an empty string. The same goes for every stage: without an instruction naming the work and the
        // re-estimation rule, a stage can be travelled through without anything happening.
        var description = epic.GetProperty("description").GetString();
        Assert.False(string.IsNullOrWhiteSpace(description));
        Assert.NotEqual("test", description);
        Assert.Equal("Epics", epic.GetProperty("title").GetString());
        Assert.Equal("layers", epic.GetProperty("icon").GetString());
        Assert.Equal("info", epic.GetProperty("color").GetString());

        var stages = epic.GetProperty("stages").EnumerateArray().ToArray();
        Assert.Equal(3, stages.Length);
        foreach (var stage in stages)
        {
            var instruction = stage.GetProperty("instruction").GetString();
            Assert.False(string.IsNullOrWhiteSpace(instruction));
            Assert.Contains("aiko_estimate_card", instruction, StringComparison.Ordinal);
            Assert.Empty(stage.GetProperty("requiredArtifacts").EnumerateArray());
        }

        // The board offers the new type the way it offers the other two, so an epic is not a card nobody can see.
        Assert.Contains(
            root.GetProperty("projections").EnumerateArray(),
            projection => string.Equals(projection.GetProperty("id").GetString(), "epics", StringComparison.Ordinal));
    }

    /// <summary>Walks up from this assembly to the solution file, as the other specs do.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(ShippedTemplateSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
