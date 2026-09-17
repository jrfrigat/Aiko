using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
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
/// these files are read by daemons, agents and editors, so they must stay plain UTF-8 without a BOM, and
/// non-ASCII text must survive a write/read round trip. A future writer that reaches for the wrong
/// overload would take the Russian content of a project with it, silently.
/// </remarks>
public sealed class ProjectFileEncodingSpecs
{
    [Fact]
    public async Task A_saved_workflow_is_utf8_without_a_bom_and_keeps_non_ascii_text()
    {
        var root = Path.Combine(Path.GetTempPath(), "Aiko.Specs", Guid.NewGuid().ToString("N"));
        var projectRoot = Path.Combine(root, "Project");
        Directory.CreateDirectory(projectRoot);

        try
        {
            var paths = new AikoDataPaths(Path.Combine(root, "data", "aiko.db"));
            var database = new AikoDatabase(paths);
            await database.InitializeAsync();
            var catalog = new SqliteProjectCatalog(database);
            var definitions = new FileProjectDefinitionStore(catalog);
            var project = await new ProjectInitializer(
                    catalog,
                    new ProjectReindexer(catalog, database),
                    new FileAppSettingsStore(catalog),
                    new FileProjectTemplateStore(paths))
                .InitializeAsync(new InitializeProjectRequest(projectRoot), CancellationToken.None);

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

            var path = Path.Combine(projectRoot, ".aiko", "workflows", "story.json");
            var bytes = await File.ReadAllBytesAsync(path);

            // A BOM is what broke the fixture: Aiko's JSON files are plain UTF-8.
            Assert.False(
                bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                "The workflow file must not start with a UTF-8 byte-order mark.");

            var text = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes);

            // The serializer escapes non-ASCII as \uXXXX by default, which is lossless but unreadable in the
            // file; what matters here is that the bytes are valid UTF-8 and the store reads back exactly what
            // it wrote.
            Assert.DoesNotContain("???", text, StringComparison.Ordinal);

            var reread = await definitions.ReadAsync(project.Id, CancellationToken.None);
            var restored = reread.Workflows.First(candidate => candidate.Id == "story");
            Assert.Equal(instruction, restored.Stages.Single(stage => stage.Order == 10).Instruction);
            Assert.Equal("Бэклог", restored.Stages.Single(stage => stage.Order == 10).Title);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
