using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
using Aiko.Domain.Prioritization;
using Aiko.Domain.Workflow;
using Aiko.Infrastructure.Projects;
using Aiko.Infrastructure.Settings;
using Aiko.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// Guards the encoding of the documents Aiko writes into a project.
/// </summary>
/// <remarks>
/// Found the hard way: a QA fixture project carried <c>?????? ????????</c> where its stage instructions
/// should have been, and the file had a byte-order mark - which Aiko never writes. The fixture had been
/// produced by a tool that lost the text before writing it, but the lesson holds for this repository:
/// these files are read by daemons, agents and editors, so they must stay plain UTF-8 without a BOM.
/// <para>
/// The rule is stricter than "no question marks". A <c>.aiko</c> tree is meant to be read and diffed by
/// people, so non-ASCII text stays in the file as its own characters: the serializer escapes every rune
/// outside ASCII unless it is told otherwise, and <c>AikoJson</c> sets
/// <c>JavaScriptEncoder.UnsafeRelaxedJsonEscaping</c> for exactly that. The escaped form is lossless, which
/// is why an earlier version of this file tolerated it - and why the assertion is here now. Both halves of
/// the tree are covered: the workflow documents and the project's own <c>settings.json</c>, which a person
/// edits through the settings screen and an agent may edit through <see cref="IAppSettingsStore"/>.
/// </para>
/// </remarks>
public sealed class ProjectFileEncodingSpecs
{
    /// <summary>Decodes strictly: a file that is not valid UTF-8 fails the frame, not the assertion inside it.</summary>
    private static readonly System.Text.UTF8Encoding Utf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    [Fact]
    public async Task A_saved_workflow_is_utf8_without_a_bom_and_keeps_non_ascii_text()
    {
        var (project, root, catalog, _) = await CreateProjectAsync();
        try
        {
            var definitions = new FileProjectDefinitionStore(catalog);
            const string instruction = "Проработай требования и архитектурные ограничения story.";
            var read = await definitions.ReadAsync(project.Id, CancellationToken.None);
            var workflow = read.Workflows.First(candidate => candidate.Id == "story");
            var stages = workflow.Stages
                .Select(stage => stage.Order == 10
                    ? stage with { Title = "Бэклог", Instruction = instruction }
                    : stage)
                .ToArray();

            await definitions.SaveWorkflowAsync(
                project.Id,
                workflow with { Stages = stages, Revision = workflow.Revision + 1 },
                workflow.Revision,
                CancellationToken.None);

            var path = Path.Combine(root, "Project", ".aiko", "workflows", "story.json");
            var bytes = await File.ReadAllBytesAsync(path);
            Assert.False(IsBom(bytes), "The workflow file must not start with a UTF-8 byte-order mark.");

            var text = Utf8.GetString(bytes);
            Assert.Contains("Бэклог", text, StringComparison.Ordinal);
            Assert.DoesNotContain("\\u04", text, StringComparison.Ordinal);
            Assert.DoesNotContain("???", text, StringComparison.Ordinal);

            var reread = await definitions.ReadAsync(project.Id, CancellationToken.None);
            var restored = reread.Workflows.First(candidate => candidate.Id == "story");
            Assert.Equal(instruction, restored.Stages.Single(stage => stage.Order == 10).Instruction);
            Assert.Equal("Бэклог", restored.Stages.Single(stage => stage.Order == 10).Title);
        }
        finally
        {
            CleanUp(root);
        }
    }

    [Fact]
    public async Task Saved_project_settings_keep_non_ascii_text_and_stay_plain_utf8()
    {
        var (project, root, _, settings) = await CreateProjectAsync();
        try
        {
            // The criterion title is the value a person reads on the card and edits in the settings screen, so
            // it is the one that hurts when a writer loses it - and the one this spec speaks in.
            const string title = "Готовность";
            var stored = await settings.ReadProjectAsync(project.Id, CancellationToken.None);
            var priority = Assert.IsType<PrioritySettings>(stored!.Priority);
            var criteria = priority.Criteria.ToArray();
            // The criterion ids are project data - the default template ships `complete`, a project may have
            // renamed it - so the criterion to rename is found by both names and the spec fails loudly if the
            // project has neither, rather than silently renaming nothing.
            var readiness = Array.FindIndex(criteria, criterion => criterion.Id is "readiness" or "complete");
            Assert.True(readiness >= 0, "The project has no readiness criterion to rename.");
            criteria[readiness] = criteria[readiness] with { Title = title };

            await settings.SaveProjectAsync(
                project.Id,
                stored with { Priority = priority with { Criteria = criteria } },
                CancellationToken.None);

            var path = Path.Combine(root, "Project", ".aiko", "settings.json");
            var bytes = await File.ReadAllBytesAsync(path);
            Assert.False(IsBom(bytes), "The settings file must not start with a UTF-8 byte-order mark.");

            var text = Utf8.GetString(bytes);
            // Readable, not merely lossless: the characters themselves, not their \uXXXX escapes.
            Assert.Contains(title, text, StringComparison.Ordinal);
            Assert.DoesNotContain("\\u04", text, StringComparison.Ordinal);
            Assert.DoesNotContain("???", text, StringComparison.Ordinal);

            var reread = await settings.ReadProjectAsync(project.Id, CancellationToken.None);
            Assert.Equal(
                title,
                reread!.Priority!.Criteria[readiness].Title);
        }
        finally
        {
            CleanUp(root);
        }
    }

    private static bool IsBom(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

    /// <summary>
    /// Registers a throwaway project with its own database, so a spec writes into a real <c>.aiko</c> tree
    /// without touching the machine's own data. The caller owns <c>Root</c> and passes it to
    /// <see cref="CleanUp"/>.
    /// </summary>
    private static async Task<(
        RegisteredProject Project,
        string Root,
        SqliteProjectCatalog Catalog,
        FileAppSettingsStore Settings)> CreateProjectAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "Aiko.Specs", Guid.NewGuid().ToString("N"));
        var projectRoot = Path.Combine(root, "Project");
        Directory.CreateDirectory(projectRoot);

        var paths = new AikoDataPaths(Path.Combine(root, "data", "aiko.db"));
        var database = new AikoDatabase(paths);
        await database.InitializeAsync();
        var catalog = new SqliteProjectCatalog(database);
        var settings = new FileAppSettingsStore(catalog);
        var project = await new ProjectInitializer(
                catalog,
                new ProjectReindexer(catalog, database),
                settings,
                new FileProjectTemplateStore(paths))
            .InitializeAsync(new InitializeProjectRequest(projectRoot), CancellationToken.None);

        return (project, root, catalog, settings);
    }

    private static void CleanUp(string root)
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
