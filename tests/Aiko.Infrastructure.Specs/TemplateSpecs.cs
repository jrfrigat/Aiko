using System.Text.Json;
using Aiko.Application.Contracts;
using Aiko.Infrastructure.Projects;
using Aiko.Infrastructure.Settings;
using Aiko.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// Guards the template contract: what a project is created from, where that material lives, and what the
/// project records about it.
/// </summary>
/// <remarks>
/// The point of the feature is that a project is autonomous after init - changing a template must reach the
/// projects created afterwards and nothing else. These specs pin the two halves of that: the copy at init,
/// and the store never overwriting a template file that already exists (a hand-edited template is the
/// installation's own, not the code's).
/// </remarks>
public sealed class TemplateSpecs
{
    [Fact]
    public async Task Init_copies_the_template_and_records_where_the_project_came_from()
    {
        var root = NewRoot();
        try
        {
            var paths = new AikoDataPaths(Path.Combine(root, "data", "aiko.db"));
            var database = new AikoDatabase(paths);
            await database.InitializeAsync();
            var catalog = new SqliteProjectCatalog(database);
            var projectRoot = Path.Combine(root, "project");
            Directory.CreateDirectory(projectRoot);

            var project = await NewInitializer(paths, database, catalog)
                .InitializeAsync(new InitializeProjectRequest(projectRoot), CancellationToken.None);

            var stitchRoot = Path.Combine(projectRoot, ".aiko");

            // The default template is written to the installation's templates root on first use...
            Assert.True(File.Exists(Path.Combine(paths.TemplatesRoot, ProjectTemplate.DefaultId, "template.json")));

            // ...its documents are copied into the project...
            Assert.True(File.Exists(Path.Combine(stitchRoot, "workflows", "story.json")));
            Assert.True(File.Exists(Path.Combine(stitchRoot, "workflows", "task.json")));
            Assert.True(File.Exists(Path.Combine(stitchRoot, "projections", "combined.json")));
            Assert.True(File.Exists(Path.Combine(stitchRoot, "memory", "index.md")));

            // ...and the manifest says which template built it, and at which version.
            using var manifest = JsonDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(stitchRoot, "project.json")));
            Assert.Equal(project.Id, manifest.RootElement.GetProperty("id").GetString());
            Assert.Equal(
                ProjectTemplate.DefaultId,
                manifest.RootElement.GetProperty("templateId").GetString());
            Assert.Equal(
                ProjectTemplate.DefaultVersion,
                manifest.RootElement.GetProperty("templateVersion").GetInt32());
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task Init_from_a_template_that_does_not_exist_fails()
    {
        var root = NewRoot();
        try
        {
            var paths = new AikoDataPaths(Path.Combine(root, "data", "aiko.db"));
            var database = new AikoDatabase(paths);
            await database.InitializeAsync();
            var catalog = new SqliteProjectCatalog(database);
            var projectRoot = Path.Combine(root, "project");
            Directory.CreateDirectory(projectRoot);

            await Assert.ThrowsAsync<FileNotFoundException>(() => NewInitializer(paths, database, catalog)
                .InitializeAsync(
                    new InitializeProjectRequest(projectRoot, TemplateId: "no-such-template"),
                    CancellationToken.None).AsTask());
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task An_existing_template_file_is_never_overwritten()
    {
        var root = NewRoot();
        try
        {
            var paths = new AikoDataPaths(Path.Combine(root, "data", "aiko.db"));
            var store = new FileProjectTemplateStore(paths);

            var builtIn = await store.EnsureDefaultAsync(CancellationToken.None);
            Assert.Equal("Default", builtIn.Name);

            // A hand-edited template is the installation's own: the next read must return it, not the
            // built-in one the store would write.
            var path = Path.Combine(paths.TemplatesRoot, ProjectTemplate.DefaultId, "template.json");
            var text = await File.ReadAllTextAsync(path);
            await File.WriteAllTextAsync(path, text.Replace("\"Default\"", "\"Hand edited\""));

            var reread = await store.EnsureDefaultAsync(CancellationToken.None);
            Assert.Equal("Hand edited", reread.Name);

            // And it is listed as the default, once, in front of anything else.
            var listed = await store.ListAsync(CancellationToken.None);
            var summary = Assert.Single(listed);
            Assert.Equal(ProjectTemplate.DefaultId, summary.Id);
            Assert.True(summary.IsDefault);
        }
        finally
        {
            Cleanup(root);
        }
    }

    private static ProjectInitializer NewInitializer(
        AikoDataPaths paths,
        AikoDatabase database,
        IProjectCatalog catalog) =>
        new(
            catalog,
            new ProjectReindexer(catalog, database),
            new FileAppSettingsStore(paths, catalog),
            new FileProjectTemplateStore(paths));

    /// <summary>An isolated directory for one spec, below the same specs root the infrastructure specs use.</summary>
    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "Aiko.Specs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>
    /// Removes one spec's directory. Pooled SQLite connections keep the database file open, so the pools are
    /// cleared first, and the delete refuses to leave the specs root.
    /// </summary>
    private static void Cleanup(string root)
    {
        SqliteConnection.ClearAllPools();
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var specsRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Aiko.Specs"))) + Path.DirectorySeparatorChar;
        if (!normalizedRoot.StartsWith(specsRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to remove unexpected test path: {normalizedRoot}");
        }

        if (Directory.Exists(normalizedRoot))
        {
            Directory.Delete(normalizedRoot, true);
        }
    }
}
