using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Aiko.Application.Contracts;
using Aiko.Application.Agents;
using Aiko.Application.Prioritization;
using Aiko.Domain.Cards;
using Aiko.Domain.Execution;
using Aiko.Domain.Workflow;
using Aiko.Infrastructure.Cards;
using Aiko.Infrastructure.Agents;
using Aiko.Infrastructure.Execution;
using Aiko.Infrastructure.Diagnostics;
using Aiko.Infrastructure.Events;
using Aiko.Infrastructure.Memory;
using Aiko.Infrastructure.Projects;
using Aiko.Infrastructure.Relations;
using Aiko.Infrastructure.Settings;
using Aiko.Infrastructure.Storage;
using Xunit;

namespace Aiko.Infrastructure.Specs;

public class InfrastructureSpecs
{
    [Fact]
    public async Task Project_initialization_is_idempotent()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var second = await context.Initializer.InitializeAsync(
                new InitializeProjectRequest(context.ProjectRoot),
                CancellationToken.None);

            Assert.Equal(context.Project.Id, second.Id);

            using var manifest = JsonDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(context.StitchRoot, "project.json")));
            Assert.Equal(1, manifest.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(context.Project.Id, manifest.RootElement.GetProperty("id").GetString());
        });
    }

    [Fact]
    public async Task Activity_report_counts_the_days_an_execution_happened()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var report = new SqliteActivityReport(context.Database);
            // Registering the project already wrote its own event, so the baseline is read rather
            // than assumed to be zero.
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var baseline = (await report.GetActivityAsync(30, CancellationToken.None))
                .Where(day => day.Date == today)
                .Sum(day => day.Count);

            var card = CreateCard(context.Project.Id, "TASK-ACTIVITY", 1);
            await context.Cards.SaveAsync(card, 0, CancellationToken.None);
            await context.Executions.StartAsync(
                card.Reference,
                "implementation",
                "claude-code",
                CancellationToken.None);

            var days = await report.GetActivityAsync(30, CancellationToken.None);

            // Sparse and ordered: days without activity are omitted rather than sent as zeroes, and
            // the dashboard fills the calendar in.
            Assert.NotEmpty(days);
            Assert.All(days, day => Assert.True(day.Date <= today));
            Assert.Equal(days.OrderBy(day => day.Date), days);

            var after = days.Where(day => day.Date == today).Sum(day => day.Count);
            Assert.True(after > baseline, $"expected today's activity to grow from {baseline}, got {after}");

            // The window is clamped in the store: a nonsensical length still answers with today.
            var clamped = await report.GetActivityAsync(0, CancellationToken.None);
            Assert.Equal(today, Assert.Single(clamped).Date);
        });
    }

    [Fact]
    public async Task Removing_a_project_registration_keeps_its_files()
    {
        await WithInitializedProjectAsync(async context =>
        {
            Assert.True(await context.Catalog.RemoveAsync(context.Project.Id, CancellationToken.None));

            Assert.Empty(await context.Catalog.ListAsync(CancellationToken.None));

            // Unregistering is not deleting: the .aiko directory and the documents in it stay, so a
            // mistyped path can be dropped without losing anything.
            Assert.True(Directory.Exists(context.StitchRoot));
            Assert.True(File.Exists(Path.Combine(context.StitchRoot, "project.json")));
            Assert.True(Directory.Exists(Path.Combine(context.StitchRoot, "workflows")));

            // A second removal reports that there was nothing left to remove.
            Assert.False(await context.Catalog.RemoveAsync(context.Project.Id, CancellationToken.None));
        });
    }

    [Fact]
    public async Task Directory_browser_lists_child_directories_only()
    {
        var browser = new DirectoryBrowser();
        var root = Path.Combine(Path.GetTempPath(), "Aiko.Specs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "beta"));
        Directory.CreateDirectory(Path.Combine(root, "alpha"));
        Directory.CreateDirectory(Path.Combine(root, "alpha", AikoProjectPaths.DirectoryName));
        await File.WriteAllTextAsync(Path.Combine(root, "notes.txt"), "not a directory");
        try
        {
            var listing = await browser.ListAsync(root, CancellationToken.None);

            Assert.Equal(Path.GetFullPath(root), listing.Path);
            Assert.NotNull(listing.Parent);
            Assert.True(listing.IsReadable);

            // Files are never listed: the picker offers project roots, not a file browser.
            Assert.Equal(
                new[] { "alpha", "beta" },
                listing.Entries.Select(entry => entry.Name));

            // A directory that already holds .aiko is marked, so a duplicate registration is visible
            // before it is made.
            var project = Assert.Single(listing.Entries, entry => entry.Name == "alpha");
            Assert.True(project.IsAikoProject);
            Assert.False(Assert.Single(listing.Entries, entry => entry.Name == "beta").IsAikoProject);
            Assert.True(project.IsReadable);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Directory_browser_rejects_paths_it_cannot_use()
    {
        var browser = new DirectoryBrowser();
        var root = Path.Combine(Path.GetTempPath(), "Aiko.Specs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "notes.txt");
        await File.WriteAllTextAsync(file, "not a directory");
        try
        {
            // A relative path would silently resolve against the daemon's working directory, a missing
            // one is a typo, and a file is not a project root: all three are user errors, not 500s.
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await browser.ListAsync("relative/path", CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await browser.ListAsync(Path.Combine(root, "missing"), CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await browser.ListAsync(file, CancellationToken.None));

            // No path is the roots screen, which is always available.
            var roots = await browser.ListAsync(null, CancellationToken.None);
            Assert.Null(roots.Path);
            Assert.NotEmpty(roots.Entries);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stale_endpoint_detection_ignores_files_that_carry_no_endpoint()
    {
        const string current = "http://127.0.0.1:5299/mcp/projects/p1";

        // A skill file carries no endpoint at all, so it is never reported as stale: an adapter also
        // writes those, and flagging them would bury the one file that matters.
        Assert.False(WorkshopDoctor.IsStaleProjectEndpoint("# /aiko-status\nShows the daemon status.", current));
        Assert.False(WorkshopDoctor.IsStaleProjectEndpoint(
            $"{{\"mcpServers\":{{\"aiko\":{{\"url\":\"{current}\"}}}}}}",
            current));

        // The project MCP configuration after a port change is exactly the drift doctor reports.
        Assert.True(WorkshopDoctor.IsStaleProjectEndpoint(
            "{\"mcpServers\":{\"aiko\":{\"url\":\"http://127.0.0.1:5260/mcp/projects/p1\"}}}",
            current));

        // User scope points at the daemon's own /mcp, and a project-scope file is not counted twice.
        Assert.True(WorkshopDoctor.IsStaleUserScopeEndpoint(
            "{\"mcpServers\":{\"aiko\":{\"url\":\"http://127.0.0.1:5260/mcp\"}}}",
            "http://127.0.0.1:5299/mcp"));
        Assert.False(WorkshopDoctor.IsStaleUserScopeEndpoint(
            "{\"mcpServers\":{\"aiko\":{\"url\":\"http://127.0.0.1:5299/mcp\"}}}",
            "http://127.0.0.1:5299/mcp"));
        Assert.False(WorkshopDoctor.IsStaleUserScopeEndpoint(
            $"{{\"url\":\"{current}\"}}",
            "http://127.0.0.1:5299/mcp"));
    }

    [Fact]
    public async Task Doctor_reports_the_installation_without_changing_it()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var dataPaths = new AikoDataPaths(context.Database.DatabasePath);
            var doctor = new WorkshopDoctor(
                dataPaths,
                context.Catalog,
                new UnifiedAgentInstaller([], context.Catalog),
                [],
                new DaemonEndpointConfiguration(dataPaths),
                new AccessTokenStore(dataPaths));

            var report = await doctor.InspectAsync(context.Project.Id, CancellationToken.None);

            // The database is there, the project is registered and present, and the two things a fresh
            // local installation is missing are named with their fix.
            Assert.Equal(DiagnosticSeverity.Ok, Assert.Single(report.Findings, finding => finding.Area == "data").Severity);
            Assert.Equal(DiagnosticSeverity.Ok, Assert.Single(report.Findings, finding => finding.Area == "project").Severity);
            Assert.Equal(DiagnosticSeverity.Error, Assert.Single(report.Findings, finding => finding.Area == "token").Severity);
            Assert.Equal(DiagnosticSeverity.Warning, Assert.Single(report.Findings, finding => finding.Area == "endpoint").Severity);
            Assert.True(report.HasProblems);

            // Nothing on disk changed: the report is the whole product of a doctor run.
            Assert.False(File.Exists(dataPaths.AccessTokenPath));
            Assert.False(File.Exists(dataPaths.SettingsPath));

            // An unknown project is an error, not an empty success.
            var unknown = await doctor.InspectAsync("nope", CancellationToken.None);
            Assert.Equal(DiagnosticSeverity.Error, Assert.Single(unknown.Findings, finding => finding.Area == "project").Severity);
        });
    }

    [Fact]
    public async Task Default_project_documents_are_created()
    {
        await WithInitializedProjectAsync(context =>
        {
            string[] requiredPaths =
            [
                "workflows/story.json",
                "workflows/task.json",
                "projections/tasks.json",
                "projections/stories.json",
                "projections/combined.json",
                "memory/index.md",
                "memory/architecture.md",
                "memory/conventions.md",
                "memory/lessons.md"
            ];

            foreach (var relativePath in requiredPaths)
            {
                Assert.True(
                    File.Exists(Path.Combine(context.StitchRoot, relativePath)),
                    $"missing default document: {relativePath}");
            }

            Assert.True(Directory.Exists(Path.Combine(context.StitchRoot, "runtime")));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Project_is_registered_in_catalog()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var projects = await context.Catalog.ListAsync(CancellationToken.None);
            Assert.Single(projects);
            Assert.Equal(context.Project.Id, projects[0].Id);
            Assert.Equal(context.ProjectRoot, projects[0].RootPath);
        });
    }

    [Fact]
    public async Task Board_definitions_include_user_workflows_and_projections()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var workflowPath = Path.Combine(context.StitchRoot, "workflows", "task.json");
            var workflow = System.Text.Json.Nodes.JsonNode.Parse(
                await File.ReadAllTextAsync(workflowPath))!.AsObject();
            workflow["stages"]!.AsArray().Add(new System.Text.Json.Nodes.JsonObject
            {
                ["id"] = "closing",
                ["title"] = "Закрытие задач",
                ["order"] = 45,
                ["instruction"] = "Обнови changelog, создай commit и PR.",
                ["allowedCardKinds"] = new System.Text.Json.Nodes.JsonArray("Task"),
                ["defaultAgentAdapterId"] = "codex",
                ["requiredArtifacts"] = new System.Text.Json.Nodes.JsonArray(),
                ["actionPolicies"] = new System.Text.Json.Nodes.JsonObject()
            });
            await File.WriteAllTextAsync(workflowPath, workflow.ToJsonString());

            var projectionPath = Path.Combine(context.StitchRoot, "projections", "agent.json");
            await File.WriteAllTextAsync(
                projectionPath,
                """
                {
                  "schemaVersion": 1,
                  "id": "agent",
                  "title": "By agent",
                  "view": "kanban",
                  "cardKind": "task",
                  "groupBy": "agent",
                  "filters": {}
                }
                """);

            var store = new FileProjectDefinitionStore(context.Catalog);
            var definition = await store.ReadAsync(context.Project.Id, CancellationToken.None);

            Assert.Contains(
                definition.Workflows.Single(item => item.Id == "task").Stages,
                stage => stage.Id == "closing" && stage.DefaultAgentAdapterId == "codex");
            Assert.Contains(
                definition.Projections,
                projection => projection.Id == "agent" && projection.GroupBy == "agent");
        });
    }

    [Fact]
    public async Task Workflow_changes_persist_with_optimistic_revisions()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var store = new FileProjectDefinitionStore(context.Catalog);
            var definition = await store.ReadAsync(context.Project.Id, CancellationToken.None);
            var workflow = definition.Workflows.Single(item => item.Id == "task");
            var closing = new StageDefinition(
                "closing",
                "Закрытие задач",
                45,
                "Обнови changelog и подготовь commit.",
                [CardKind.Task],
                "codex",
                [new ArtifactRequirement("closing.md", "Итог закрытия.", MissingArtifactPolicy.Warn)],
                new Dictionary<string, ActionPolicy>(StringComparer.Ordinal));
            var updated = workflow with
            {
                Stages = [.. workflow.Stages, closing],
                Revision = workflow.Revision + 1
            };

            await store.SaveWorkflowAsync(
                context.Project.Id,
                updated,
                workflow.Revision,
                CancellationToken.None);
            var persisted = (await store.ReadAsync(context.Project.Id, CancellationToken.None))
                .Workflows.Single(item => item.Id == "task");
            Assert.Equal(updated.Revision, persisted.Revision);
            Assert.Contains(
                persisted.Stages,
                stage => stage.Id == "closing" &&
                    stage.RequiredArtifacts.Single().Path == "closing.md");

            var conflict = await Assert.ThrowsAsync<RevisionConflictException>(async () =>
                await store.SaveWorkflowAsync(
                    context.Project.Id,
                    updated,
                    workflow.Revision,
                    CancellationToken.None));
            Assert.Equal(persisted.Revision, conflict.ActualRevision);
        });
    }

    [Fact]
    public async Task Git_ignore_entry_is_idempotent()
    {
        await WithInitializedProjectAsync(async context =>
        {
            await context.Initializer.InitializeAsync(
                new InitializeProjectRequest(context.ProjectRoot),
                CancellationToken.None);

            var lines = await File.ReadAllLinesAsync(Path.Combine(context.ProjectRoot, ".gitignore"));
            Assert.Equal(1, lines.Count(line => line == "/.aiko/"));
        });
    }

    [Fact]
    public async Task Card_artifacts_stay_confined_and_reject_stale_saves()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var card = CreateCard(context.Project.Id, "TASK-ARTIFACT", 1);
            await context.Cards.SaveAsync(card, 0, CancellationToken.None);
            var artifacts = new FileCardArtifactStore(context.Catalog, context.Cards);

            var saved = await artifacts.SaveAsync(
                card.Reference,
                "notes/analysis.md",
                "# Анализ\n",
                null,
                CancellationToken.None);
            var loaded = await artifacts.ReadAsync(
                card.Reference,
                "notes/analysis.md",
                CancellationToken.None);
            Assert.Equal(saved.Version, loaded!.Version);
            Assert.Equal("# Анализ\n", loaded.Content);
            var listed = await artifacts.ListAsync(card.Reference, CancellationToken.None);
            Assert.Equal("notes/analysis.md", listed.Single().Path);

            var conflict = await Assert.ThrowsAsync<ArtifactConflictException>(async () =>
                await artifacts.SaveAsync(
                    card.Reference,
                    "notes/analysis.md",
                    "stale",
                    null,
                    CancellationToken.None));
            Assert.Equal(saved.Version, conflict.ActualVersion);

            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await artifacts.ReadAsync(card.Reference, "../outside.md", CancellationToken.None));
            Assert.False(
                File.Exists(Path.Combine(context.StitchRoot, "tasks", "outside.md")),
                "artifact escaped its card directory");
        });
    }

    [Fact]
    public async Task Card_files_and_projection_stay_consistent()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var reference = new CardReference(context.Project.Id, "TASK-001");
            var firstRevision = new Card(
                reference,
                CardKind.Task,
                "Implement storage",
                "task",
                "implementation",
                1,
                10m,
                ["src/**"],
                [],
                new Dictionary<string, string>());

            await context.Cards.SaveAsync(firstRevision, 0, CancellationToken.None);

            var saved = await context.Cards.FindAsync(reference, CancellationToken.None);
            Assert.Equal(1L, saved?.Revision);
            Assert.True(
                File.Exists(Path.Combine(
                    context.StitchRoot,
                    "tasks",
                    reference.CardId,
                    "card.json")),
                "card.json was not created");

            var listed = await context.Cards.ListAsync(context.Project.Id, CancellationToken.None);
            Assert.Single(listed);
            Assert.Equal(reference.CardId, listed[0].Reference.CardId);

            var secondRevision = firstRevision with
            {
                Title = "Implement durable storage",
                Revision = 2,
                ActualChangedFiles = ["src/Aiko.Infrastructure/Cards/FileCardStore.cs"]
            };
            await context.Cards.SaveAsync(secondRevision, 1, CancellationToken.None);

            var conflict = await Assert.ThrowsAsync<RevisionConflictException>(async () =>
                await context.Cards.SaveAsync(secondRevision, 1, CancellationToken.None));
            Assert.Equal(1L, conflict.ExpectedRevision);
            Assert.Equal(2L, conflict.ActualRevision);
        });
    }

    [Fact]
    public async Task Relations_reject_blocks_cycles()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var first = CreateCard(context.Project.Id, "TASK-A", 1);
            var second = CreateCard(context.Project.Id, "TASK-B", 1);
            await context.Cards.SaveAsync(first, 0, CancellationToken.None);
            await context.Cards.SaveAsync(second, 0, CancellationToken.None);

            await context.Relations.SaveAsync(
                new CardRelation(
                    "REL-1",
                    first.Reference,
                    second.Reference,
                    RelationTypes.Blocks,
                    DateTimeOffset.UtcNow),
                CancellationToken.None);

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await context.Relations.SaveAsync(
                    new CardRelation(
                        "REL-2",
                        second.Reference,
                        first.Reference,
                        RelationTypes.Blocks,
                        DateTimeOffset.UtcNow),
                    CancellationToken.None));

            var relations = await context.Relations.ListAsync(
                context.Project.Id,
                CancellationToken.None);
            Assert.Single(relations);
        });
    }

    [Fact]
    public async Task Memory_is_searchable_and_path_confined()
    {
        await WithInitializedProjectAsync(async context =>
        {
            await context.Memory.StoreAsync(
                context.Project.Id,
                "decisions/storage.md",
                "# Storage\n\nSQLite is a rebuildable projection.",
                CancellationToken.None);
            var results = await context.Memory.SearchAsync(
                context.Project.Id,
                "rebuildable",
                10,
                CancellationToken.None);
            Assert.Single(results);
            Assert.Equal("decisions/storage.md", results[0].Path);

            var prefixResults = await context.Memory.SearchAsync(
                context.Project.Id,
                "rebuild",
                10,
                CancellationToken.None);
            Assert.Single(prefixResults);

            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await context.Memory.StoreAsync(
                    context.Project.Id,
                    "../outside.md",
                    "unsafe",
                    CancellationToken.None));
        });
    }

    [Fact]
    public async Task Settings_resolve_project_over_global_over_default()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var store = new FileAppSettingsStore(
                new AikoDataPaths(context.Database.DatabasePath),
                context.Catalog);
            var service = new AppSettingsService(store);

            var fallback = await service.LoadAsync(context.Project.Id, CancellationToken.None);
            Assert.Equal(ExecutionSettings.SafeDefault, fallback.EffectiveExecution);
            Assert.Equal(AppSettingsSources.Default, fallback.ExecutionSource);

            await service.SaveGlobalAsync(
                new AppSettings(
                    AppSettings.CurrentSchemaVersion,
                    new ExecutionSettings(WorkspaceMode.Shared, 3, ActionPolicy.Ask, ActionPolicy.Deny)),
                CancellationToken.None);
            var fromGlobal = await service.LoadAsync(context.Project.Id, CancellationToken.None);
            Assert.Equal(3, fromGlobal.EffectiveExecution.MaxConcurrentRuns);
            Assert.Equal(AppSettingsSources.Global, fromGlobal.ExecutionSource);

            await service.SaveProjectAsync(
                context.Project.Id,
                new AppSettings(
                    AppSettings.CurrentSchemaVersion,
                    new ExecutionSettings(WorkspaceMode.Shared, 2, ActionPolicy.Allow, ActionPolicy.Deny)),
                CancellationToken.None);
            var fromProject = await service.LoadAsync(context.Project.Id, CancellationToken.None);
            Assert.Equal(2, fromProject.EffectiveExecution.MaxConcurrentRuns);
            Assert.Equal(ActionPolicy.Allow, fromProject.EffectiveExecution.ScopeOverlapPolicy);
            Assert.Equal(AppSettingsSources.Project, fromProject.ExecutionSource);
            Assert.NotNull(fromProject.Global);
            Assert.Equal(3, fromProject.Global!.Execution!.MaxConcurrentRuns);
            Assert.True(File.Exists(Path.Combine(context.StitchRoot, "settings.json")));

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await service.SaveProjectAsync(
                    context.Project.Id,
                    new AppSettings(
                        AppSettings.CurrentSchemaVersion,
                        new ExecutionSettings(WorkspaceMode.Shared, 0, ActionPolicy.Ask, ActionPolicy.Deny)),
                    CancellationToken.None));
        });
    }

    [Fact]
    public async Task Board_priorities_combine_task_and_parent_values()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var story = new Card(
                new CardReference(context.Project.Id, "STORY-PRIORITY"),
                CardKind.Story,
                "Storage story",
                "story",
                "backlog",
                1,
                8m,
                [],
                [],
                new Dictionary<string, string>());
            var task = new Card(
                new CardReference(context.Project.Id, "TASK-PRIORITY"),
                CardKind.Task,
                "Storage task",
                "task",
                "backlog",
                1,
                4m,
                [],
                [],
                new Dictionary<string, string>());
            await context.Cards.SaveAsync(story, 0, CancellationToken.None);
            await context.Cards.SaveAsync(task, 0, CancellationToken.None);
            await context.Relations.SaveAsync(
                new CardRelation(
                    "REL-PRIORITY",
                    story.Reference,
                    task.Reference,
                    RelationTypes.ParentChild,
                    DateTimeOffset.UtcNow),
                CancellationToken.None);

            var priorities = CardPriorityProjector.Project(
                await context.Cards.ListAsync(context.Project.Id, CancellationToken.None),
                await context.Relations.ListAsync(context.Project.Id, CancellationToken.None));

            var taskSnapshot = priorities
                .Single(priority => priority.CardId == task.Reference.CardId)
                .Snapshot;
            Assert.Equal(8m, taskSnapshot.MaximumParentPriority);
            Assert.Equal(5.2m, taskSnapshot.EffectivePriority);
            var storySnapshot = priorities
                .Single(priority => priority.CardId == story.Reference.CardId)
                .Snapshot;
            Assert.Equal(8m, storySnapshot.EffectivePriority);
        });
    }

    [Fact]
    public async Task Sqlite_can_be_rebuilt_from_project_files()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var first = CreateCard(context.Project.Id, "TASK-REINDEX", 1);
            var second = CreateCard(context.Project.Id, "TASK-DEPENDENCY", 1);
            await context.Cards.SaveAsync(first, 0, CancellationToken.None);
            await context.Cards.SaveAsync(second, 0, CancellationToken.None);
            await context.Relations.SaveAsync(
                new CardRelation(
                    "REL-REINDEX",
                    first.Reference,
                    second.Reference,
                    RelationTypes.ParentChild,
                    DateTimeOffset.UtcNow),
                CancellationToken.None);
            await context.Memory.StoreAsync(
                context.Project.Id,
                "reindex.md",
                "Durable reindex marker.",
                CancellationToken.None);

            SqliteConnection.ClearAllPools();
            File.Delete(context.Database.DatabasePath);
            File.Delete($"{context.Database.DatabasePath}-wal");
            File.Delete($"{context.Database.DatabasePath}-shm");

            await context.Database.InitializeAsync();
            await context.Catalog.SaveAsync(context.Project, CancellationToken.None);
            await context.Reindexer.ReindexAsync(context.Project.Id, CancellationToken.None);

            var cards = await context.Cards.ListAsync(context.Project.Id, CancellationToken.None);
            Assert.Equal(2, cards.Count);
            await using (var connection = context.Database.CreateConnection())
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText =
                    "SELECT COUNT(*) FROM relations WHERE project_id = $projectId;";
                command.Parameters.AddWithValue("$projectId", context.Project.Id);
                Assert.Equal(1L, (long)(await command.ExecuteScalarAsync() ?? 0L));
            }

            var memory = await context.Memory.SearchAsync(
                context.Project.Id,
                "reindex marker",
                10,
                CancellationToken.None);
            Assert.Single(memory);
        });
    }

    [Fact]
    public async Task Events_are_persisted_broadcast_and_replayed_by_id()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var broadcaster = new AikoEventBroadcaster();
            using var live = broadcaster.Subscribe(context.Project.Id);
            var publisher = new SqliteAikoEventPublisher(context.Database, broadcaster);

            var first = await publisher.PublishAsync(
                context.Project.Id, AikoEventTypes.CardUpdated, "{}", CancellationToken.None);
            var second = await publisher.PublishAsync(
                context.Project.Id, AikoEventTypes.RelationsUpdated, string.Empty, CancellationToken.None);

            Assert.True(first.Id < second.Id);
            Assert.True(
                live.Events.TryRead(out var received) &&
                received.Id == first.Id &&
                received.Type == AikoEventTypes.CardUpdated);

            var all = await context.EventJournal.ReadAsync(
                context.Project.Id, afterId: 0, limit: 10, CancellationToken.None);
            Assert.Equal(2, all.Count);
            Assert.Equal(first.Id, all[0].Id);
            var afterFirst = await context.EventJournal.ReadAsync(
                context.Project.Id, afterId: first.Id, limit: 10, CancellationToken.None);
            var tail = Assert.Single(afterFirst);
            Assert.Equal(second.Id, tail.Id);
            Assert.Equal(AikoEventTypes.RelationsUpdated, tail.Type);
        });
    }

    [Fact]
    public async Task Card_relation_and_execution_changes_publish_events()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var first = CreateCard(context.Project.Id, "TASK-EVENT", 1);
            var second = CreateCard(context.Project.Id, "STORY-EVENT", 1) with
            {
                Kind = CardKind.Story,
                WorkflowId = "story"
            };
            await context.Cards.SaveAsync(first, 0, CancellationToken.None);
            await context.Cards.SaveAsync(second, 0, CancellationToken.None);
            await context.Relations.SaveAsync(
                new CardRelation(
                    "REL-EVENT",
                    second.Reference,
                    first.Reference,
                    RelationTypes.ParentChild,
                    DateTimeOffset.UtcNow),
                CancellationToken.None);
            await context.Executions.StartAsync(
                first.Reference, "implementation", "claude-code", CancellationToken.None);

            var events = await context.EventJournal.ReadAsync(
                context.Project.Id, afterId: 0, limit: 50, CancellationToken.None);
            Assert.Contains(events, item =>
                item.Type == AikoEventTypes.CardUpdated &&
                item.PayloadJson.Contains("TASK-EVENT", StringComparison.Ordinal));
            Assert.Contains(events, item => item.Type == AikoEventTypes.RelationsUpdated);
            var executionEvent = Assert.Single(events, item =>
                item.Type == AikoEventTypes.ExecutionUpdated);
            Assert.Contains(
                "claude-code",
                executionEvent.PayloadJson,
                StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task User_scope_install_creates_and_removes_global_skills()
    {
        var previousHome = Environment.GetEnvironmentVariable("AIKO_USER_HOME");
        var fakeHome = Path.Combine(Path.GetTempPath(), "Aiko.Specs", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("AIKO_USER_HOME", fakeHome);
        try
        {
            var adapter = new ClaudeCodeAgentAdapter();
            var applied = await adapter.ApplyUserInstallAsync(CancellationToken.None);
            Assert.True(applied.Succeeded);
            Assert.True(File.Exists(Path.Combine(fakeHome, ".claude", "skills", "aiko", "SKILL.md")));
            Assert.True(File.Exists(Path.Combine(fakeHome, ".claude", "commands", "aiko-init.md")));

            var removed = await adapter.UninstallUserAsync(CancellationToken.None);
            Assert.True(removed.Succeeded);
            Assert.False(File.Exists(Path.Combine(fakeHome, ".claude", "skills", "aiko", "SKILL.md")));
            Assert.False(File.Exists(Path.Combine(fakeHome, ".claude", "commands", "aiko-init.md")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("AIKO_USER_HOME", previousHome);
        }
    }

    [Fact]
    public async Task Codex_user_scope_install_writes_the_clients_own_skills_root()
    {
        var previousHome = Environment.GetEnvironmentVariable("AIKO_USER_HOME");
        var fakeHome = Path.Combine(Path.GetTempPath(), "Aiko.Specs", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("AIKO_USER_HOME", fakeHome);
        try
        {
            var adapter = new CodexAgentAdapter();
            var applied = await adapter.ApplyUserInstallAsync(CancellationToken.None);
            Assert.True(applied.Succeeded);

            // The client's own root: this is where Codex looks for user-scope skills, so a skill that
            // only landed in the portable .agents tree was effectively invisible.
            Assert.True(File.Exists(Path.Combine(fakeHome, ".codex", "skills", "aiko", "SKILL.md")));
            // The portable location is still written, and stays byte-identical to the client's copy.
            var portable = Path.Combine(fakeHome, ".agents", "skills", "aiko", "SKILL.md");
            Assert.True(File.Exists(portable));
            Assert.Equal(
                await File.ReadAllTextAsync(Path.Combine(fakeHome, ".codex", "skills", "aiko", "SKILL.md")),
                await File.ReadAllTextAsync(portable));

            var removed = await adapter.UninstallUserAsync(CancellationToken.None);
            Assert.True(removed.Succeeded);
            Assert.False(File.Exists(Path.Combine(fakeHome, ".codex", "skills", "aiko", "SKILL.md")));
            Assert.False(File.Exists(portable));
        }
        finally
        {
            Environment.SetEnvironmentVariable("AIKO_USER_HOME", previousHome);
        }
    }

    [Fact]
    public async Task Project_initialization_copies_global_defaults()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var store = new FileAppSettingsStore(new AikoDataPaths(context.Database.DatabasePath), context.Catalog);
            var service = new AppSettingsService(store);
            await service.SaveGlobalAsync(
                new AppSettings(
                    AppSettings.CurrentSchemaVersion,
                    new ExecutionSettings(WorkspaceMode.Shared, 3, ActionPolicy.Ask, ActionPolicy.Deny),
                    new Aiko.Domain.Prioritization.PrioritySettings(new Aiko.Domain.Prioritization.PriorityWeights(0.5m, 0.5m), [])),
                CancellationToken.None);

            var newRoot = Path.Combine(context.ProjectRoot, "new-project");
            Directory.CreateDirectory(newRoot);
            var newProject = await context.Initializer.InitializeAsync(
                new InitializeProjectRequest(newRoot),
                CancellationToken.None);

            var view = await service.LoadAsync(newProject.Id, CancellationToken.None);
            Assert.Equal(3, view.EffectiveExecution.MaxConcurrentRuns);
            Assert.Equal(0.5m, view.EffectivePriority.Weights.TaskWeight);
        });
    }

    [Fact]
    public async Task Project_priority_weights_override_the_default()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var store = new FileAppSettingsStore(new AikoDataPaths(context.Database.DatabasePath), context.Catalog);
            var service = new AppSettingsService(store);

            var fallback = await service.GetEffectivePriorityAsync(context.Project.Id, CancellationToken.None);
            Assert.Equal(0.7m, fallback.Weights.TaskWeight);

            await service.SaveProjectAsync(
                context.Project.Id,
                new AppSettings(AppSettings.CurrentSchemaVersion, null, new Aiko.Domain.Prioritization.PrioritySettings(new Aiko.Domain.Prioritization.PriorityWeights(0.5m, 0.5m), [])),
                CancellationToken.None);
            var project = await service.GetEffectivePriorityAsync(context.Project.Id, CancellationToken.None);
            Assert.Equal(0.5m, project.Weights.TaskWeight);
        });
    }

    [Fact]
    public async Task Access_token_is_generated_and_reused()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var paths = new AikoDataPaths(context.Database.DatabasePath);
            var store = new AccessTokenStore(paths);
            var first = await store.GetOrCreateAsync(CancellationToken.None);
            Assert.False(string.IsNullOrWhiteSpace(first));
            var second = await store.GetOrCreateAsync(CancellationToken.None);
            Assert.Equal(first, second);
            Assert.True(File.Exists(paths.AccessTokenPath));
        });
    }

    [Fact]
    public async Task Commit_policy_deny_ask_allow_is_enforced()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var settings = new AppSettingsService(new FileAppSettingsStore(
                new AikoDataPaths(context.Database.DatabasePath), context.Catalog));
            var executions = new SqliteExecutionCoordinator(
                context.Catalog, context.Cards, context.Database, settings);
            var card = CreateCard(context.Project.Id, "TASK-COMMIT", 1);
            await context.Cards.SaveAsync(card, 0, CancellationToken.None);
            var started = await executions.StartAsync(card.Reference, "implementation", "claude-code", CancellationToken.None);

            // Default policy is Deny.
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await executions.ReportCommitAsync(started.Id, "abc", "fix", ["src/a.cs"], CancellationToken.None));

            // Ask: records a pending request and waits for the user.
            await settings.SaveProjectAsync(context.Project.Id,
                new AppSettings(AppSettings.CurrentSchemaVersion,
                    new ExecutionSettings(WorkspaceMode.Shared, 1, ActionPolicy.Ask, ActionPolicy.Ask)),
                CancellationToken.None);
            var asked = await executions.ReportCommitAsync(started.Id, null, "fix", ["src/a.cs"], CancellationToken.None);
            Assert.Equal(StageExecutionState.WaitingForUser, asked.State);
            Assert.Single(asked.Commits);
            Assert.Equal(CommitState.PendingApproval, asked.Commits[0].State);

            var approved = await executions.ApproveCommitAsync(started.Id, true, CancellationToken.None);
            Assert.Equal(StageExecutionState.Running, approved.State);
            Assert.Equal(CommitState.Recorded, approved.Commits[0].State);

            // Allow: records the commit directly.
            await settings.SaveProjectAsync(context.Project.Id,
                new AppSettings(AppSettings.CurrentSchemaVersion,
                    new ExecutionSettings(WorkspaceMode.Shared, 1, ActionPolicy.Ask, ActionPolicy.Allow)),
                CancellationToken.None);
            var allowed = await executions.ReportCommitAsync(started.Id, "def", "fix2", ["src/b.cs"], CancellationToken.None);
            Assert.Equal(StageExecutionState.Running, allowed.State);
            Assert.Equal(2, allowed.Commits.Count);
            Assert.Equal(CommitState.Recorded, allowed.Commits[1].State);
            Assert.Equal("def", allowed.Commits[1].Sha);
        });
    }

    [Fact]
    public async Task Overlapping_scope_is_gated_by_scope_overlap_policy()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var settings = new AppSettingsService(new FileAppSettingsStore(
                new AikoDataPaths(context.Database.DatabasePath), context.Catalog));
            await settings.SaveProjectAsync(
                context.Project.Id,
                new AppSettings(
                    AppSettings.CurrentSchemaVersion,
                    new ExecutionSettings(WorkspaceMode.Shared, 2, ActionPolicy.Deny, ActionPolicy.Deny)),
                CancellationToken.None);
            var executions = new SqliteExecutionCoordinator(
                context.Catalog, context.Cards, context.Database, settings);

            var first = CreateCard(context.Project.Id, "TASK-OVL-A", 1) with { DeclaredScopeFiles = ["src/**"] };
            var second = CreateCard(context.Project.Id, "TASK-OVL-B", 1) with { DeclaredScopeFiles = ["src/Foo/**"] };
            var third = CreateCard(context.Project.Id, "TASK-OVL-C", 1) with { DeclaredScopeFiles = ["tests/**"] };
            await context.Cards.SaveAsync(first, 0, CancellationToken.None);
            await context.Cards.SaveAsync(second, 0, CancellationToken.None);
            await context.Cards.SaveAsync(third, 0, CancellationToken.None);

            await executions.StartAsync(first.Reference, "implementation", "claude-code", CancellationToken.None);

            var denied = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await executions.StartAsync(second.Reference, "implementation", "codex", CancellationToken.None));
            Assert.Contains("scope", denied.Message, StringComparison.OrdinalIgnoreCase);

            await executions.StartAsync(third.Reference, "implementation", "codex", CancellationToken.None);
        });
    }

    [Fact]
    public async Task Concurrent_run_limit_is_enforced_from_effective_settings()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var settings = new AppSettingsService(new FileAppSettingsStore(
                new AikoDataPaths(context.Database.DatabasePath), context.Catalog));
            var executions = new SqliteExecutionCoordinator(
                context.Catalog, context.Cards, context.Database, settings);
            var first = CreateCard(context.Project.Id, "TASK-LIMIT-A", 1);
            var second = CreateCard(context.Project.Id, "TASK-LIMIT-B", 1);
            var third = CreateCard(context.Project.Id, "TASK-LIMIT-C", 1);
            await context.Cards.SaveAsync(first, 0, CancellationToken.None);
            await context.Cards.SaveAsync(second, 0, CancellationToken.None);
            await context.Cards.SaveAsync(third, 0, CancellationToken.None);

            await executions.StartAsync(
                first.Reference, "implementation", "claude-code", CancellationToken.None);

            var rejection = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await executions.StartAsync(
                    second.Reference, "implementation", "codex", CancellationToken.None));
            Assert.Contains("maxConcurrentRuns", rejection.Message, StringComparison.Ordinal);

            await settings.SaveProjectAsync(
                context.Project.Id,
                new AppSettings(
                    AppSettings.CurrentSchemaVersion,
                    new ExecutionSettings(WorkspaceMode.Shared, 2, ActionPolicy.Ask, ActionPolicy.Deny)),
                CancellationToken.None);
            await executions.StartAsync(
                second.Reference, "implementation", "codex", CancellationToken.None);

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await executions.StartAsync(
                    third.Reference, "implementation", "cursor", CancellationToken.None));
        });
    }


    [Fact]
    public async Task Concurrent_starts_on_different_cards_never_exceed_the_run_limit()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var cards = new Card[8];
            for (var index = 0; index < cards.Length; index++)
            {
                cards[index] = CreateCard(context.Project.Id, $"TASK-RACE-{index}", 1);
                await context.Cards.SaveAsync(cards[index], 0, CancellationToken.None);
            }

            var starts = cards
                .Select(card => context.Executions.StartAsync(
                    card.Reference, "implementation", "codex", CancellationToken.None).AsTask())
                .ToArray();

            var succeeded = 0;
            foreach (var start in starts)
            {
                try
                {
                    await start;
                    succeeded++;
                }
                catch (InvalidOperationException)
                {
                    // Rejected because the single-run limit was reached.
                }
            }

            Assert.Equal(1, succeeded);
        });
    }

    [Fact]
    public async Task Board_priority_propagates_through_multiple_parent_levels()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var story = new Card(
                new CardReference(context.Project.Id, "STORY-DEEP"),
                CardKind.Story, "story", "story", "backlog", 1, 10m, [], [], new Dictionary<string, string>());
            var task = new Card(
                new CardReference(context.Project.Id, "TASK-DEEP"),
                CardKind.Task, "task", "task", "backlog", 1, 4m, [], [], new Dictionary<string, string>());
            var subtask = new Card(
                new CardReference(context.Project.Id, "SUB-DEEP"),
                CardKind.Task, "subtask", "task", "backlog", 1, 1m, [], [], new Dictionary<string, string>());
            await context.Cards.SaveAsync(story, 0, CancellationToken.None);
            await context.Cards.SaveAsync(task, 0, CancellationToken.None);
            await context.Cards.SaveAsync(subtask, 0, CancellationToken.None);
            await context.Relations.SaveAsync(
                new CardRelation("R-DEEP-1", story.Reference, task.Reference, RelationTypes.ParentChild, DateTimeOffset.UtcNow),
                CancellationToken.None);
            await context.Relations.SaveAsync(
                new CardRelation("R-DEEP-2", task.Reference, subtask.Reference, RelationTypes.ParentChild, DateTimeOffset.UtcNow),
                CancellationToken.None);

            var priorities = CardPriorityProjector.Project(
                await context.Cards.ListAsync(context.Project.Id, CancellationToken.None),
                await context.Relations.ListAsync(context.Project.Id, CancellationToken.None));

            var taskSnapshot = priorities.Single(priority => priority.CardId == "TASK-DEEP").Snapshot;
            Assert.Equal(5.8m, taskSnapshot.EffectivePriority);
            Assert.Equal(10m, taskSnapshot.MaximumParentPriority);

            var subtaskSnapshot = priorities.Single(priority => priority.CardId == "SUB-DEEP").Snapshot;
            Assert.Equal(5.8m, subtaskSnapshot.MaximumParentPriority);
            Assert.Equal(2.44m, subtaskSnapshot.EffectivePriority);
        });
    }

    [Fact]
    public async Task Reindex_returns_counts_of_rebuilt_records()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var card = CreateCard(context.Project.Id, "TASK-REINDEX-COUNT", 1);
            await context.Cards.SaveAsync(card, 0, CancellationToken.None);
            await context.Memory.StoreAsync(context.Project.Id, "extra.md", "Extra memory.", CancellationToken.None);

            var result = await context.Reindexer.ReindexAsync(context.Project.Id, CancellationToken.None);

            Assert.Equal(1, result.Cards);
            Assert.Equal(0, result.Relations);
            Assert.True(result.MemoryDocuments >= 1);
        });
    }

    [Fact]
    public async Task Duplicate_card_id_across_collections_is_rejected()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var stories = Path.Combine(context.StitchRoot, "stories", "DUP-1");
            var tasks = Path.Combine(context.StitchRoot, "tasks", "DUP-1");
            Directory.CreateDirectory(stories);
            Directory.CreateDirectory(tasks);
            var story = CreateCard(context.Project.Id, "DUP-1", 1) with { Kind = CardKind.Story };
            var task = CreateCard(context.Project.Id, "DUP-1", 1);
            await File.WriteAllTextAsync(
                Path.Combine(stories, "card.json"),
                JsonSerializer.Serialize(story, options));
            await File.WriteAllTextAsync(
                Path.Combine(tasks, "card.json"),
                JsonSerializer.Serialize(task, options));

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await context.Reindexer.ReindexAsync(context.Project.Id, CancellationToken.None));
        });
    }

    [Fact]
    public async Task Foreign_keys_are_enforced_on_every_connection()
    {
        await WithInitializedProjectAsync(async context =>
        {
            await using var connection = context.Database.CreateConnection();
            await connection.OpenAsync();

            await using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA foreign_keys;";
                Assert.Equal(1L, (long)(await pragma.ExecuteScalarAsync() ?? 0L));
            }

            await using (var insert = connection.CreateCommand())
            {
                insert.CommandText =
                    """
                    INSERT INTO cards(project_id, card_id, kind, title, stage_id, revision, document_json, updated_utc)
                    VALUES ('missing', 'TASK-FK', 'Task', 'Foreign key probe', 'backlog', 1, '{}', '2026-01-01T00:00:00Z');
                    """;
                await Assert.ThrowsAsync<SqliteException>(
                    async () => await insert.ExecuteNonQueryAsync());
            }
        });
    }

    [Fact]
    public async Task Legacy_memory_documents_are_migrated_to_fts5_once()
    {
        await WithInitializedProjectAsync(async context =>
        {
            await using (var setup = context.Database.CreateConnection())
            {
                await setup.OpenAsync();
                await using var command = setup.CreateCommand();
                command.CommandText =
                    """
                    CREATE TABLE memory_documents (
                        project_id TEXT NOT NULL,
                        path TEXT NOT NULL,
                        content TEXT NOT NULL,
                        updated_utc TEXT NOT NULL,
                        PRIMARY KEY(project_id, path)
                    );
                    INSERT INTO memory_documents(project_id, path, content, updated_utc)
                        VALUES ('legacy', 'lessons.md', 'Old durable memory.', '2026-01-01T00:00:00Z');
                    DELETE FROM memory_fts;
                    DELETE FROM schema_migrations WHERE version = 5;
                    """;
                await command.ExecuteNonQueryAsync();
            }

            await context.Database.InitializeAsync();

            await using (var verify = context.Database.CreateConnection())
            {
                await verify.OpenAsync();

                await using (var legacy = verify.CreateCommand())
                {
                    legacy.CommandText =
                        "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'memory_documents';";
                    Assert.Equal(0L, (long)(await legacy.ExecuteScalarAsync() ?? 0L));
                }

                await using (var fts = verify.CreateCommand())
                {
                    fts.CommandText =
                        "SELECT COUNT(*) FROM memory_fts WHERE project_id = 'legacy' AND path = 'lessons.md';";
                    Assert.Equal(1L, (long)(await fts.ExecuteScalarAsync() ?? 0L));
                }

                await using (var version = verify.CreateCommand())
                {
                    version.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE version = 5;";
                    Assert.Equal(1L, (long)(await version.ExecuteScalarAsync() ?? 0L));
                }
            }
        });
    }

    [Fact]
    public async Task Rate_limited_execution_hands_off_without_losing_history()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var card = CreateCard(context.Project.Id, "TASK-HANDOFF", 1) with
            {
                DeclaredScopeFiles = ["src/**"]
            };
            await context.Cards.SaveAsync(card, 0, CancellationToken.None);

            var started = await context.Executions.StartAsync(
                card.Reference,
                "implementation",
                "claude-code",
                CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await context.Executions.StartAsync(
                    card.Reference,
                    "implementation",
                    "codex",
                    CancellationToken.None));

            await context.Executions.ReportProgressAsync(
                started.Id,
                "Storage implementation is half complete.",
                ["Added schema"],
                ["Add tests"],
                ["src/Storage.cs"],
                CancellationToken.None);
            await context.Executions.ReportAgentStateAsync(
                started.Id,
                AgentAttemptState.RateLimited,
                "Claude usage limit reached.",
                CancellationToken.None);

            var handedOff = await context.Executions.HandoffAsync(
                started.Id,
                "codex",
                CancellationToken.None);
            Assert.Equal(started.Id, handedOff.Id);
            Assert.Equal(2, handedOff.Attempts.Count);
            Assert.Equal(AgentAttemptState.RateLimited, handedOff.Attempts[0].State);
            Assert.Equal("codex", handedOff.Attempts[1].AgentAdapterId);

            var completed = await context.Executions.CompleteAsync(
                started.Id,
                ["src/Storage.cs", "docs/storage.md"],
                ["implementation.md"],
                CancellationToken.None);
            Assert.Equal(StageExecutionState.Completed, completed.State);
            var outOfScope = Assert.Single(completed.OutOfScopeFiles);
            Assert.Equal("docs/storage.md", outOfScope);

            var savedCard = await context.Cards.FindAsync(card.Reference, CancellationToken.None);
            Assert.Equal(3L, savedCard?.Revision);
            Assert.Equal(2, savedCard?.ActualChangedFiles.Count);

            var handoffDirectory = Path.Combine(
                context.StitchRoot,
                "tasks",
                card.Reference.CardId,
                "handoffs");
            Assert.Single(Directory.GetFiles(handoffDirectory, "*.md"));
        });
    }

    [Fact]
    public async Task Agent_detection_reports_one_executable_per_name()
    {
        // An npm install on Windows leaves an extensionless shell shim next to its .cmd, and the detector
        // reported both: one agent looked like two installations in the UI.
        var directory = Path.Combine(Path.GetTempPath(), "Aiko.Specs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "zcode"), string.Empty);
        await File.WriteAllTextAsync(Path.Combine(directory, "zcode.cmd"), string.Empty);

        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            // PATH is replaced rather than prepended, so the result does not depend on whether the machine
            // running the tests happens to have a real zcode installed.
            Environment.SetEnvironmentVariable("PATH", directory);
            var installations = await new ZCodeAgentAdapter().DetectInstallationsAsync(CancellationToken.None);

            var installation = Assert.Single(installations);
            Assert.Equal(Path.Combine(directory, "zcode.cmd"), installation.ExecutablePath);
            // Discovery scans PATH; it does not run the agent, so the version stays unknown.
            Assert.Null(installation.Version);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task User_scope_state_says_whether_aiko_is_connected_to_an_agent()
    {
        var home = Path.Combine(Path.GetTempPath(), "Aiko.Specs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);
        var originalHome = Environment.GetEnvironmentVariable("AIKO_USER_HOME");
        try
        {
            Environment.SetEnvironmentVariable("AIKO_USER_HOME", home);
            await WithInitializedProjectAsync(async context =>
            {
                IAgentAdapter[] adapters = [new ZCodeAgentAdapter()];
                var installer = new UnifiedAgentInstaller(adapters, context.Catalog);

                // Nothing written yet: the state says "not connected" rather than pretending the adapter
                // is absent - the two are different facts.
                var before = Assert.Single(await installer.DiscoverAsync(CancellationToken.None));
                Assert.True(before.UserScope is { } expected && expected.ExpectedFiles > 0);
                Assert.Equal(0, before.UserScope?.ConfiguredFiles);

                var applied = await installer.ApplyUserInstallAsync("zcode", CancellationToken.None);
                Assert.NotNull(applied);
                Assert.True(applied!.Succeeded);

                var after = Assert.Single(await installer.DiscoverAsync(CancellationToken.None));
                Assert.True(after.UserScope?.IsConfigured());

                // Disconnect removes only Aiko's files, and an unknown adapter is not an operation.
                Assert.NotNull(await installer.UninstallUserAsync("zcode", CancellationToken.None));
                var uninstalled = Assert.Single(await installer.DiscoverAsync(CancellationToken.None));
                Assert.Equal(0, uninstalled.UserScope?.ConfiguredFiles);
                Assert.Null(await installer.ApplyUserInstallAsync("nope", CancellationToken.None));
                Assert.Null(await installer.UninstallUserAsync("nope", CancellationToken.None));
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("AIKO_USER_HOME", originalHome);
            Directory.Delete(home, true);
        }
    }


    [Fact]
    public async Task Agent_configuration_carries_the_access_token()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var installer = new UnifiedAgentInstaller(
                [new ClaudeCodeAgentAdapter(), new CodexAgentAdapter()],
                context.Catalog);

            var applied = await installer.ApplyAsync(
                context.Project.Id,
                $"http://127.0.0.1:18471/mcp/projects/{context.Project.Id}",
                "test-token",
                ["claude-code", "codex"],
                CancellationToken.None);
            Assert.All(applied.AdapterResults, result => Assert.True(result.Succeeded));

            // The daemon authenticates /mcp with this header: a configuration without it answers 401, which
            // is why the token belongs in the file the client reads.
            var claude = await File.ReadAllTextAsync(
                Path.Combine(context.Project.RootPath, ".mcp.json"));
            Assert.Contains("Bearer test-token", claude, StringComparison.Ordinal);
            Assert.Contains("Authorization", claude, StringComparison.Ordinal);
            // Claude Code tags its own streamable HTTP entries, so Aiko writes the same shape.
            Assert.Contains("\"type\": \"http\"", claude, StringComparison.Ordinal);

            // Codex cannot hold a literal header; it gets the variable name its own CLI writes.
            var codex = await File.ReadAllTextAsync(
                Path.Combine(context.Project.RootPath, ".codex", "config.toml"));
            Assert.Contains("bearer_token_env_var = \"AIKO_TOKEN\"", codex, StringComparison.Ordinal);

            Assert.True(WorkshopDoctor.HasEndpoint(claude));
            Assert.True(WorkshopDoctor.HasCredential(claude));
            Assert.True(WorkshopDoctor.HasCredential(codex));
        });
    }

    [Fact]
    public async Task Doctor_reports_a_configuration_that_cannot_authenticate()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Aiko.Specs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "claude.cmd"), string.Empty);

        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            // A fake claude on PATH: the drift check only looks at adapters that are installed, and a
            // machine running the tests may have none.
            Environment.SetEnvironmentVariable("PATH", directory);
            await WithInitializedProjectAsync(async context =>
            {
                var dataPaths = new AikoDataPaths(context.Database.DatabasePath);
                // The daemon writes its own settings when it starts; here the production writer does it, so
                // the file has exactly the shape the daemon reads back.
                var configuration = new DaemonEndpointConfiguration(dataPaths);
                var settings = await configuration.LoadOrCreateAsync(null, CancellationToken.None);

                // An installation from before the token existed: the endpoint is right, nothing
                // authenticates, so the daemon answers 401 and the agent never sees Aiko.
                var mcp = Path.Combine(context.Project.RootPath, ".mcp.json");
                await File.WriteAllTextAsync(
                    mcp,
                    $"{{\"mcpServers\":{{\"aiko\":{{\"url\":\"http://127.0.0.1:{settings.Port}/mcp/projects/{context.Project.Id}\"}}}}}}");

                var doctor = new WorkshopDoctor(
                    dataPaths,
                    context.Catalog,
                    new UnifiedAgentInstaller([new ClaudeCodeAgentAdapter()], context.Catalog),
                    [new ClaudeCodeAgentAdapter()],
                    configuration,
                    new AccessTokenStore(dataPaths));

                var report = await doctor.InspectAsync(context.Project.Id, CancellationToken.None);

                var finding = Assert.Single(report.Findings, item => item.Area == "agent-config");
                Assert.Contains("no access token", finding.Summary, StringComparison.Ordinal);
                Assert.Equal(mcp, finding.Detail);
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Unified_installer_groups_selected_plans()
    {
        await WithInitializedProjectAsync(async context =>
        {
            IAgentAdapter[] adapters =
            [
                new ClaudeCodeAgentAdapter(),
                new CodexAgentAdapter(),
                new CursorAgentAdapter(),
                new ZCodeAgentAdapter()
            ];
            var installer = new UnifiedAgentInstaller(adapters, context.Catalog);
            var options = await installer.DiscoverAsync(CancellationToken.None);
            Assert.Equal(4, options.Count);

            var plan = await installer.PlanAsync(
                context.Project.Id,
                $"http://127.0.0.1:18471/mcp/projects/{context.Project.Id}",
                "test-token",
                ["codex", "zcode", "future-agent"],
                CancellationToken.None);
            Assert.Equal(2, plan.AdapterPlans.Count);
            var unknown = Assert.Single(plan.UnknownAdapterIds);
            Assert.Equal("future-agent", unknown);
            Assert.Contains(
                plan.AdapterPlans
                    .Single(item => item.AdapterId == "codex")
                    .Changes,
                change => change.Path.EndsWith(
                    Path.Combine(".codex", "config.toml"),
                    StringComparison.OrdinalIgnoreCase));
            Assert.Contains(
                plan.AdapterPlans
                    .Single(item => item.AdapterId == "zcode")
                    .Changes,
                change => change.Path.EndsWith(
                    Path.Combine(".zcode", "config.json"),
                    StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public async Task Unified_installer_applies_idempotently_without_replacing_user_settings()
    {
        await WithInitializedProjectAsync(async context =>
        {
            IAgentAdapter[] adapters =
            [
                new CodexAgentAdapter(),
                new CursorAgentAdapter(),
                new ZCodeAgentAdapter()
            ];
            var installer = new UnifiedAgentInstaller(adapters, context.Catalog);
            var codexConfig = Path.Combine(context.Project.RootPath, ".codex", "config.toml");
            var cursorConfig = Path.Combine(context.Project.RootPath, ".cursor", "mcp.json");
            var zcodeConfig = Path.Combine(context.Project.RootPath, ".zcode", "config.json");
            var agentsFile = Path.Combine(context.Project.RootPath, "AGENTS.md");
            Directory.CreateDirectory(Path.GetDirectoryName(codexConfig)!);
            Directory.CreateDirectory(Path.GetDirectoryName(cursorConfig)!);
            Directory.CreateDirectory(Path.GetDirectoryName(zcodeConfig)!);
            await File.WriteAllTextAsync(codexConfig, "model = \"existing-model\"\n");
            await File.WriteAllTextAsync(
                cursorConfig,
                "{\"custom\":true,\"mcpServers\":{\"other\":{\"url\":\"http://127.0.0.1:9\"}}}");
            await File.WriteAllTextAsync(
                zcodeConfig,
                "{\"theme\":\"keep\",\"mcp\":{\"servers\":{\"other\":{\"url\":\"http://127.0.0.1:8\"}}}}");
            await File.WriteAllTextAsync(agentsFile, "# User instructions\n\nKeep this text.\n");

            const string endpoint = "http://127.0.0.1:18471/mcp/projects/test";
            var first = await installer.ApplyAsync(
                context.Project.Id,
                endpoint,
                "test-token",
                ["codex", "cursor", "zcode"],
                CancellationToken.None);
            Assert.All(first.AdapterResults, result => Assert.True(result.Succeeded));

            var second = await installer.ApplyAsync(
                context.Project.Id,
                endpoint,
                "test-token",
                ["codex", "cursor", "zcode"],
                CancellationToken.None);
            Assert.All(
                second.AdapterResults.SelectMany(result => result.Files),
                file => Assert.Equal(InstallationFileStatus.Unchanged, file.Status));

            var installedCodex = await File.ReadAllTextAsync(codexConfig);
            Assert.Contains("existing-model", installedCodex, StringComparison.Ordinal);
            Assert.Contains("[mcp_servers.aiko]", installedCodex, StringComparison.Ordinal);
            var installedAgents = await File.ReadAllTextAsync(agentsFile);
            Assert.Contains("Keep this text.", installedAgents, StringComparison.Ordinal);

            using var cursorDocument = JsonDocument.Parse(await File.ReadAllTextAsync(cursorConfig));
            Assert.True(cursorDocument.RootElement.GetProperty("custom").GetBoolean());
            Assert.Equal(
                endpoint,
                cursorDocument.RootElement
                    .GetProperty("mcpServers")
                    .GetProperty("aiko")
                    .GetProperty("url")
                    .GetString());
            Assert.True(
                cursorDocument.RootElement
                    .GetProperty("mcpServers")
                    .TryGetProperty("other", out _));

            using var zcodeDocument = JsonDocument.Parse(await File.ReadAllTextAsync(zcodeConfig));
            Assert.Equal("keep", zcodeDocument.RootElement.GetProperty("theme").GetString());
            var zcodeServers = zcodeDocument.RootElement
                .GetProperty("mcp")
                .GetProperty("servers");
            Assert.Equal(
                endpoint,
                zcodeServers.GetProperty("aiko").GetProperty("url").GetString());
            Assert.True(zcodeServers.TryGetProperty("other", out _));
        });
    }

    [Fact]
    public async Task Unified_installer_removes_only_aiko_managed_content()
    {
        await WithInitializedProjectAsync(async context =>
        {
            IAgentAdapter[] adapters =
            [
                new CodexAgentAdapter(),
                new CursorAgentAdapter()
            ];
            var installer = new UnifiedAgentInstaller(adapters, context.Catalog);
            var codexConfig = Path.Combine(context.Project.RootPath, ".codex", "config.toml");
            var cursorConfig = Path.Combine(context.Project.RootPath, ".cursor", "mcp.json");
            var cursorRule = Path.Combine(context.Project.RootPath, ".cursor", "rules", "aiko.mdc");
            var agentsFile = Path.Combine(context.Project.RootPath, "AGENTS.md");
            Directory.CreateDirectory(Path.GetDirectoryName(codexConfig)!);
            Directory.CreateDirectory(Path.GetDirectoryName(cursorConfig)!);
            await File.WriteAllTextAsync(codexConfig, "model = \"keep-me\"\n");
            await File.WriteAllTextAsync(
                cursorConfig,
                "{\"mcpServers\":{\"other\":{\"url\":\"http://127.0.0.1:9\"}}}");
            await File.WriteAllTextAsync(agentsFile, "# Keep these instructions\n");

            await installer.ApplyAsync(
                context.Project.Id,
                "http://127.0.0.1:18471/mcp/projects/test",
                "test-token",
                ["codex", "cursor"],
                CancellationToken.None);
            var preview = await installer.PlanUninstallAsync(
                context.Project.Id,
                ["codex", "cursor"],
                CancellationToken.None);
            Assert.Equal(2, preview.AdapterPlans.Count);

            var removed = await installer.UninstallAsync(
                context.Project.Id,
                ["codex", "cursor"],
                CancellationToken.None);
            Assert.All(removed.AdapterResults, result => Assert.True(result.Succeeded));
            Assert.False(File.Exists(cursorRule));

            var installedCodex = await File.ReadAllTextAsync(codexConfig);
            Assert.Contains("keep-me", installedCodex, StringComparison.Ordinal);
            Assert.DoesNotContain("mcp_servers.aiko", installedCodex, StringComparison.Ordinal);
            var installedAgents = await File.ReadAllTextAsync(agentsFile);
            Assert.Contains("Keep these instructions", installedAgents, StringComparison.Ordinal);
            Assert.DoesNotContain("aiko:begin", installedAgents, StringComparison.Ordinal);

            using (var cursorDocument = JsonDocument.Parse(await File.ReadAllTextAsync(cursorConfig)))
            {
                var servers = cursorDocument.RootElement.GetProperty("mcpServers");
                Assert.False(servers.TryGetProperty("aiko", out _));
                Assert.True(servers.TryGetProperty("other", out _));
            }

            var repeated = await installer.UninstallAsync(
                context.Project.Id,
                ["codex", "cursor"],
                CancellationToken.None);
            Assert.All(
                repeated.AdapterResults.SelectMany(result => result.Files),
                file => Assert.Equal(InstallationFileStatus.Unchanged, file.Status));

            Directory.CreateDirectory(Path.GetDirectoryName(cursorRule)!);
            await File.WriteAllTextAsync(cursorRule, "User-owned rule");
            var protectedResult = await installer.UninstallAsync(
                context.Project.Id,
                ["cursor"],
                CancellationToken.None);
            Assert.False(protectedResult.AdapterResults.Single().Succeeded);
            Assert.Equal("User-owned rule", await File.ReadAllTextAsync(cursorRule));
        });
    }

    [Fact]
    public async Task Daemon_prefers_24560_and_persists_an_available_fallback()
    {
        await WithInitializedProjectAsync(async context =>
        {
            TcpListener? preferredPortBlocker = null;
            try
            {
                preferredPortBlocker = new TcpListener(
                    IPAddress.Loopback,
                    DaemonEndpointConfiguration.PreferredPort);
                preferredPortBlocker.Start();
            }
            catch (SocketException)
            {
                preferredPortBlocker?.Stop();
                preferredPortBlocker = null;
            }
            var preferredPortWasAvailable = preferredPortBlocker is not null;

            var paths = new AikoDataPaths(context.Database.DatabasePath);
            var configuration = new DaemonEndpointConfiguration(paths);
            var first = await configuration.LoadOrCreateAsync(null, CancellationToken.None);
            Assert.InRange(
                first.Port,
                DaemonEndpointConfiguration.FirstAutomaticPort,
                DaemonEndpointConfiguration.LastAutomaticPort);
            preferredPortBlocker?.Stop();
            Assert.True(File.Exists(paths.SettingsPath), "daemon settings were not persisted");

            var second = await configuration.LoadOrCreateAsync(null, CancellationToken.None);
            Assert.Equal(first.Port, second.Port);

            var listener = new TcpListener(IPAddress.Loopback, first.Port);
            listener.Start();
            try
            {
                var failure = await Assert.ThrowsAsync<IOException>(async () =>
                    await configuration.LoadOrCreateAsync(null, CancellationToken.None));
                Assert.Contains("installer", failure.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                listener.Stop();
            }

            var customPortProbe = new TcpListener(IPAddress.Loopback, 0);
            customPortProbe.Start();
            var customPort = ((IPEndPoint)customPortProbe.LocalEndpoint).Port;
            customPortProbe.Stop();
            var customized = await configuration.LoadOrCreateAsync(
                customPort,
                CancellationToken.None);
            Assert.Equal(customPort, customized.Port);
            var persistedCustom = await configuration.LoadOrCreateAsync(null, CancellationToken.None);
            Assert.Equal(customPort, persistedCustom.Port);

            if (preferredPortWasAvailable)
            {
                var preferredPaths = new AikoDataPaths(
                    Path.Combine(context.ProjectRoot, "preferred-port", "aiko.db"));
                var preferredConfiguration = new DaemonEndpointConfiguration(preferredPaths);
                var preferred = await preferredConfiguration.LoadOrCreateAsync(null, CancellationToken.None);
                Assert.Equal(DaemonEndpointConfiguration.PreferredPort, preferred.Port);
            }
        });
    }

    private static Card CreateCard(string projectId, string cardId, long revision) =>
        new(
            new CardReference(projectId, cardId),
            CardKind.Task,
            cardId,
            "task",
            "backlog",
            revision,
            1m,
            [],
            [],
            new Dictionary<string, string>());

    private static async Task WithInitializedProjectAsync(Func<TestContext, Task> assertion)
    {
        var specsRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Aiko.Specs"));
        var testRoot = Path.Combine(specsRoot, Guid.NewGuid().ToString("N"));
        var projectRoot = Path.Combine(testRoot, "Project");
        Directory.CreateDirectory(projectRoot);

        try
        {
            var database = new AikoDatabase(
                new AikoDataPaths(Path.Combine(testRoot, "data", "aiko.db")));
            await database.InitializeAsync();

            var catalog = new SqliteProjectCatalog(database);
            var reindexer = new ProjectReindexer(catalog, database);
            var appSettingsStore = new FileAppSettingsStore(
                new AikoDataPaths(Path.Combine(testRoot, "data", "aiko.db")), catalog);
            var initializer = new ProjectInitializer(catalog, reindexer, appSettingsStore);
            var project = await initializer.InitializeAsync(
                new InitializeProjectRequest(projectRoot),
                CancellationToken.None);
            var eventPublisher = new SqliteAikoEventPublisher(
                database, new AikoEventBroadcaster());
            var appSettings = new AppSettingsService(appSettingsStore);

            var cards = new FileCardStore(catalog, database, eventPublisher);
            var executions = new SqliteExecutionCoordinator(
                catalog, cards, database, appSettings, eventPublisher);
            await assertion(new TestContext(
                projectRoot,
                Path.Combine(projectRoot, ".aiko"),
                project,
                catalog,
                initializer,
                cards,
                new FileRelationStore(catalog, cards, database, eventPublisher),
                new FileMemoryStore(catalog, database),
                reindexer,
                database,
                executions,
                eventPublisher,
                new SqliteAikoEventStore(database)));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(testRoot));
            var safePrefix = Path.TrimEndingDirectorySeparator(specsRoot) + Path.DirectorySeparatorChar;
            if (!normalizedRoot.StartsWith(safePrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Refusing to remove unexpected test path: {normalizedRoot}");
            }

            if (Directory.Exists(normalizedRoot))
            {
                Directory.Delete(normalizedRoot, true);
            }
        }
    }
}
