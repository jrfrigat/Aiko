using System.Text.Json;
using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
using Aiko.Infrastructure.Cards;
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

    [Fact]
    public async Task Init_takes_the_git_policy_from_the_template_when_the_request_does_not_say()
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

            // A template that tracks project knowledge: an init that only names it takes its policy.
            var templates = new FileProjectTemplateStore(paths);
            var builtIn = await templates.EnsureDefaultAsync(CancellationToken.None);
            await templates.WriteAsync(
                builtIn with { Id = "tracked", Name = "Tracked", GitPolicy = ProjectGitPolicy.TrackProjectKnowledge },
                CancellationToken.None);

            await NewInitializer(paths, database, catalog).InitializeAsync(
                new InitializeProjectRequest(projectRoot, TemplateId: "tracked"),
                CancellationToken.None);

            using var manifest = JsonDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(projectRoot, ".aiko", "project.json")));
            Assert.Equal("tracked", manifest.RootElement.GetProperty("templateId").GetString());
            Assert.Equal(
                nameof(ProjectGitPolicy.TrackProjectKnowledge),
                manifest.RootElement.GetProperty("gitPolicy").GetString());
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task A_template_is_captured_from_a_project_with_its_content_and_provenance()
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
                .InitializeAsync(new InitializeProjectRequest(projectRoot, Name: "Shop"), CancellationToken.None);

            var templates = new FileProjectTemplateStore(paths);
            var captured = await templates.CreateFromProjectAsync(
                projectRoot,
                "shop-default",
                name: null,
                CancellationToken.None);

            Assert.Equal("shop-default", captured.Id);
            Assert.Equal("Shop template", captured.Name);
            Assert.NotEmpty(captured.Workflows);
            Assert.NotEmpty(captured.Projections);
            Assert.NotEmpty(captured.MemoryFiles);
            // The capture is a snapshot of the project's own settings, not of the template it came from.
            Assert.NotNull(await new FileAppSettingsStore(catalog)
                .ReadProjectAsync(project.Id, CancellationToken.None));
            Assert.NotNull(captured.Settings);

            // It is a template of this installation from now on, and it is listed.
            var listed = await templates.ListAsync(CancellationToken.None);
            Assert.Contains(listed, summary => summary.Id == "shop-default" && !summary.IsBuiltIn);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task Exporting_and_importing_a_template_round_trips_its_content()
    {
        var root = NewRoot();
        try
        {
            var paths = new AikoDataPaths(Path.Combine(root, "data", "aiko.db"));
            var store = new FileProjectTemplateStore(paths);
            var builtIn = await store.EnsureDefaultAsync(CancellationToken.None);
            var source = builtIn with { Id = "shared", Name = "Shared", Description = "Handed over." };
            await store.WriteAsync(source, CancellationToken.None);

            var exportPath = Path.Combine(root, "export", "shared.json");
            await store.ExportAsync("shared", exportPath, CancellationToken.None);
            Assert.True(File.Exists(exportPath));

            // A second installation is this one with an empty templates root: the file is the template.
            var fresh = new FileProjectTemplateStore(
                new AikoDataPaths(Path.Combine(root, "other", "aiko.db")));
            var imported = await fresh.ImportAsync(exportPath, templateId: null, CancellationToken.None);

            Assert.Equal("shared", imported.Id);
            Assert.Equal("Shared", imported.Name);
            Assert.Equal(source.Workflows.Count, imported.Workflows.Count);
            Assert.Equal(source.Projections.Count, imported.Projections.Count);
            Assert.Equal(source.MemoryFiles.Count, imported.MemoryFiles.Count);

            // Importing the same file twice under the same id is refused: nothing is overwritten by accident.
            await Assert.ThrowsAsync<IOException>(() => fresh
                .ImportAsync(exportPath, templateId: null, CancellationToken.None).AsTask());
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task Applying_a_template_replaces_the_pipeline_and_refuses_to_strand_a_card()
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

            var templates = new FileProjectTemplateStore(paths);
            var builtIn = await templates.EnsureDefaultAsync(CancellationToken.None);

            // A narrower template: the task pipeline loses its review stage.
            var taskWorkflow = builtIn.Workflows.Single(workflow => workflow.Id == "task");
            var narrowTask = taskWorkflow with
            {
                Stages = taskWorkflow.Stages
                    .Where(stage => !string.Equals(stage.Id, "review", StringComparison.Ordinal))
                    .ToArray()
            };
            await templates.WriteAsync(
                builtIn with
                {
                    Id = "narrow",
                    Name = "Narrow",
                    Workflows = builtIn.Workflows
                        .Select(workflow => StringComparer.Ordinal.Equals(workflow.Id, "task") ? narrowTask : workflow)
                        .ToArray()
                },
                CancellationToken.None);

            var cards = new FileCardStore(catalog, database);
            var definitions = new FileProjectDefinitionStore(catalog);
            var applier = new ProjectTemplateApplier(
                catalog,
                definitions,
                cards,
                new FileAppSettingsStore(catalog),
                templates);

            // With no card in the stage the template drops, the apply goes through.
            var applied = await applier.ApplyAsync(project.Id, "narrow", CancellationToken.None);
            Assert.Equal("narrow", applied.Id);
            var afterApply = await definitions.ReadAsync(project.Id, CancellationToken.None);
            Assert.DoesNotContain(
                afterApply.Workflows.Single(workflow => workflow.Id == "task").Stages,
                stage => stage.Id == "review");

            // A card in that stage stops the next one: work is not silently pushed out of its pipeline.
            await cards.SaveAsync(
                new Card(
                    new CardReference(project.Id, "FL-1"),
                    CardKind.Task,
                    "A card in review",
                    "task",
                    "review",
                    1,
                    1m,
                    [],
                    [],
                    new Dictionary<string, string>(StringComparer.Ordinal)),
                0,
                CancellationToken.None);

            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                applier.ApplyAsync(project.Id, "narrow", CancellationToken.None).AsTask());
            Assert.Contains("review", failure.Message, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task A_stored_template_is_deleted_and_the_base_is_always_available()
    {
        var root = NewRoot();
        try
        {
            var paths = new AikoDataPaths(Path.Combine(root, "data", "aiko.db"));
            var store = new FileProjectTemplateStore(paths);
            var builtIn = await store.EnsureDefaultAsync(CancellationToken.None);
            await store.WriteAsync(builtIn with { Id = "scratch", Name = "Scratch" }, CancellationToken.None);

            Assert.True(await store.DeleteAsync("scratch", CancellationToken.None));
            Assert.DoesNotContain(await store.ListAsync(CancellationToken.None), summary => summary.Id == "scratch");
            // A template that was never stored has nothing to remove.
            Assert.False(await store.DeleteAsync("absent", CancellationToken.None));
            // The base is a file after its first use: deleting it removes that file, and the built-in one
            // takes its place again, because an installation always has a base.
            Assert.True(await store.DeleteAsync(ProjectTemplate.DefaultId, CancellationToken.None));
            Assert.Equal("Default", (await store.ReadAsync(ProjectTemplate.DefaultId, CancellationToken.None)).Name);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task A_template_initialization_instruction_is_copied_into_the_new_project()
    {
        var root = NewRoot();
        try
        {
            var paths = new AikoDataPaths(Path.Combine(root, "data", "aiko.db"));
            var database = new AikoDatabase(paths);
            await database.InitializeAsync();
            var catalog = new SqliteProjectCatalog(database);
            var templates = new FileProjectTemplateStore(paths);

            // A template of this installation that asks for something beyond the files Aiko copies: the
            // structure a project of this kind should have.
            var origin = await templates.ReadAsync(ProjectTemplate.DefaultId, CancellationToken.None);
            await templates.WriteAsync(
                origin with
                {
                    Id = "structured",
                    Name = "Structured",
                    InitializationInstruction = "Create src/, tests/ and docs/, and add an .editorconfig."
                },
                CancellationToken.None);

            var projectRoot = Path.Combine(root, "project");
            Directory.CreateDirectory(projectRoot);
            await NewInitializer(paths, database, catalog).InitializeAsync(
                new InitializeProjectRequest(projectRoot, TemplateId: "structured"),
                CancellationToken.None);

            // The project owns its copy as a document, so a person can read why the project was scaffolded
            // this way, and a later edit to the template never reaches back into it.
            var document = Path.Combine(projectRoot, ".aiko", "initialization.md");
            Assert.True(File.Exists(document));
            Assert.Contains(
                "Create src/, tests/ and docs/",
                await File.ReadAllTextAsync(document),
                StringComparison.Ordinal);

            // A template without one writes nothing, so a project created from the built-in default has no
            // empty instruction to read and none to ignore.
            var plainRoot = Path.Combine(root, "plain");
            Directory.CreateDirectory(plainRoot);
            await NewInitializer(paths, database, catalog).InitializeAsync(
                new InitializeProjectRequest(plainRoot, TemplateId: ProjectTemplate.DefaultId),
                CancellationToken.None);
            Assert.False(File.Exists(Path.Combine(plainRoot, ".aiko", "initialization.md")));
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
            new FileAppSettingsStore(catalog),
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
