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
using Aiko.Infrastructure.Commands;
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
    public async Task A_project_gets_a_readable_handle_from_its_name()
    {
        await WithInitializedProjectAsync(async context =>
        {
            // The name and the handle both exist, and the handle follows from the name; the temp directory
            // carries a GUID, so the value itself is not predicted here, only its relationship.
            Assert.False(string.IsNullOrWhiteSpace(context.Project.Slug));
            Assert.Equal(ProjectSlug.Derive(context.Project.Name), context.Project.Slug);

            using var manifest = JsonDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(context.StitchRoot, "project.json")));
            Assert.Equal(context.Project.Slug, manifest.RootElement.GetProperty("slug").GetString());

            // Both identifiers resolve, which is what keeps existing id-based links working.
            Assert.Equal(context.Project.Id,
                (await context.Catalog.FindAsync(context.Project.Slug!, CancellationToken.None))!.Id);
            Assert.Equal(context.Project.Id,
                (await context.Catalog.FindAsync(context.Project.Id, CancellationToken.None))!.Id);
        });
    }

    [Fact]
    public async Task A_handle_the_caller_chose_is_used_as_is_or_refused()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var chosenRoot = Path.Combine(Path.GetTempPath(), "Aiko.Specs", Guid.NewGuid().ToString("N"));
            var clashRoot = Path.Combine(Path.GetTempPath(), "Aiko.Specs", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(chosenRoot);
            Directory.CreateDirectory(clashRoot);
            try
            {
                var chosen = await context.Initializer.InitializeAsync(
                    new InitializeProjectRequest(chosenRoot, "Мой пеРвыЙ проект", Slug: "moj-pervyj-proekt"),
                    CancellationToken.None);
                Assert.Equal("moj-pervyj-proekt", chosen.Slug);
                Assert.Equal("Мой пеРвыЙ проект", chosen.Name);

                // A value the caller typed is never silently changed: the clash is an error naming it, so
                // the user learns which project already holds the handle.
                var taken = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await context.Initializer.InitializeAsync(
                        new InitializeProjectRequest(clashRoot, Slug: "moj-pervyj-proekt"),
                        CancellationToken.None));
                Assert.Contains("moj-pervyj-proekt", taken.Message, StringComparison.Ordinal);
            }
            finally
            {
                Directory.Delete(chosenRoot, true);
                Directory.Delete(clashRoot, true);
            }
        });
    }

    [Fact]
    public async Task A_derived_handle_is_made_unique_instead_of_failing()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var secondRoot = Path.Combine(Path.GetTempPath(), "Aiko.Specs", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(secondRoot);
            try
            {
                // Same name as the first project and no handle given: the second gets "-2" rather than an
                // error, because the user only picked a folder.
                var second = await context.Initializer.InitializeAsync(
                    new InitializeProjectRequest(secondRoot, context.Project.Name),
                    CancellationToken.None);
                Assert.Equal($"{context.Project.Slug}-2", second.Slug);
            }
            finally
            {
                Directory.Delete(secondRoot, true);
            }
        });
    }

    [Fact]
    public async Task A_project_without_a_handle_gets_one_from_its_name()
    {
        await WithInitializedProjectAsync(async context =>
        {
            // What an installation upgraded from an older release looks like: neither the manifest nor the
            // registration carries a handle.
            var manifestPath = Path.Combine(context.StitchRoot, "project.json");
            var document = System.Text.Json.Nodes.JsonNode.Parse(
                await File.ReadAllTextAsync(manifestPath))!.AsObject();
            document.Remove("slug");
            await File.WriteAllTextAsync(manifestPath, document.ToJsonString());
            await context.Catalog.SaveAsync(context.Project with { Slug = null }, CancellationToken.None);

            var assigned = await context.Initializer.EnsureSlugsAsync(CancellationToken.None);
            Assert.Equal(1, assigned);

            var repaired = Assert.Single(await context.Catalog.ListAsync(CancellationToken.None));
            Assert.Equal(ProjectSlug.Derive(repaired.Name), repaired.Slug);

            // And it is idempotent: a project that already has a handle keeps it.
            Assert.Equal(0, await context.Initializer.EnsureSlugsAsync(CancellationToken.None));
            Assert.Equal(
                repaired.Slug,
                Assert.Single(await context.Catalog.ListAsync(CancellationToken.None)).Slug);
        });
    }

    [Fact]
    public async Task Activity_report_counts_the_days_an_execution_happened()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var report = new SqliteActivityReport(context.Database, context.Catalog);
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
    public void The_project_endpoint_carries_the_readable_handle()
    {
        var registered = new RegisteredProject("01a0b2c3d4e5f60718293a4b5c6d7e8f", "Demo", @"C:\Demo", "demo");

        // The handle is what a person reads in an agent's configuration, so a URL says which project it
        // belongs to instead of carrying a GUID nobody can place.
        Assert.Equal(
            "http://127.0.0.1:5299/mcp/projects/demo",
            ProjectMcpEndpoint.For("http://127.0.0.1:5299", registered));
        // A daemon address that already ends in a slash does not double up.
        Assert.Equal(
            "http://127.0.0.1:5299/mcp/projects/demo",
            ProjectMcpEndpoint.For("http://127.0.0.1:5299/", registered));

        // A project registered before slugs existed keeps working: the id resolves just the same.
        var legacy = new RegisteredProject("01a0b2c3d4e5f60718293a4b5c6d7e8f", "Demo", @"C:\Demo");
        Assert.Equal(
            "http://127.0.0.1:5299/mcp/projects/01a0b2c3d4e5f60718293a4b5c6d7e8f",
            ProjectMcpEndpoint.For("http://127.0.0.1:5299", legacy));
    }

    [Fact]
    public async Task Install_and_diagnosis_agree_on_the_project_endpoint()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Aiko.Specs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "claude.cmd"), string.Empty);
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", directory);
            await WithInitializedProjectAsync(async context =>
            {
                // What this locks: install and repair used to build the path themselves and disagree - one
                // wrote the project's id, the other the handle a user had typed - so they rewrote each
                // other's files on every run and the diagnosis called a working configuration stale.
                var dataPaths = new AikoDataPaths(context.Database.DatabasePath);
                var configuration = new DaemonEndpointConfiguration(dataPaths);
                var settings = await configuration.LoadOrCreateAsync(null, CancellationToken.None);
                var adapter = new ClaudeCodeAgentAdapter();
                var installer = new UnifiedAgentInstaller(
                    [adapter], context.Catalog, new FileProjectDefinitionStore(context.Catalog));

                await installer.ApplyAsync(
                    context.Project.Id,
                    ProjectMcpEndpoint.For($"http://127.0.0.1:{settings.Port}", context.Project),
                    "test-token",
                    ["claude-code"],
                    CancellationToken.None);

                var doctor = new WorkshopDoctor(
                    dataPaths,
                    context.Catalog,
                    installer,
                    [adapter],
                    configuration,
                    new AccessTokenStore(dataPaths),
                    context.Cards,
                    new FileProjectDefinitionStore(context.Catalog),
                    context.Executions);
                var report = await doctor.InspectAsync(context.Project.Id, CancellationToken.None);

                // A configuration this build wrote is never reported as drift.
                Assert.DoesNotContain(report.Findings, finding => finding.Area == "agent-config");
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            Directory.Delete(directory, true);
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
    public async Task A_working_directory_resolves_to_the_project_that_owns_it()
    {
        await WithInitializedProjectAsync(async context =>
        {
            // The project itself...
            var exact = await ProjectPathLookup.ResolveAsync(
                context.Catalog, context.ProjectRoot, CancellationToken.None);
            Assert.Equal(ProjectPathMatch.Registered, exact.Match);
            Assert.Equal(context.Project.Id, exact.Project?.Id);

            // ...and a folder inside it: opening a subfolder is not opening a different project.
            var nested = Path.Combine(context.ProjectRoot, "src", "nested");
            Directory.CreateDirectory(nested);
            var inner = await ProjectPathLookup.ResolveAsync(
                context.Catalog, nested, CancellationToken.None);
            Assert.Equal(ProjectPathMatch.Registered, inner.Match);
            Assert.Equal(context.Project.RootPath, inner.Project?.RootPath);

            // A folder nobody registered says so instead of guessing at a project.
            var loose = Path.GetFullPath(Path.Combine(context.ProjectRoot, "..", "not-a-project"));
            Directory.CreateDirectory(loose);
            var none = await ProjectPathLookup.ResolveAsync(
                context.Catalog, loose, CancellationToken.None);
            Assert.Equal(ProjectPathMatch.None, none.Match);
            Assert.Null(none.Project);
        });
    }

    [Fact]
    public async Task A_folder_that_carries_aiko_without_being_registered_says_which_it_is()
    {
        await WithInitializedProjectAsync(async context =>
        {
            // What a project copied from another machine, or one whose registration was removed, looks like:
            // the folder is an Aiko project, the daemon simply has no record of it. The two have to be told
            // apart, because only one of them is fixed by registering.
            var stray = Path.GetFullPath(Path.Combine(context.ProjectRoot, "..", "stray"));
            Directory.CreateDirectory(AikoProjectPaths.DataRoot(stray));

            var result = await ProjectPathLookup.ResolveAsync(
                context.Catalog, stray, CancellationToken.None);

            Assert.Equal(ProjectPathMatch.Initialized, result.Match);
            Assert.Null(result.Project);
            Assert.Equal(stray, result.Path);
        });
    }

    [Fact]
    public async Task Global_procedures_start_by_working_out_which_project_they_are_in()
    {
        var previousHome = Environment.GetEnvironmentVariable("AIKO_USER_HOME");
        var fakeHome = Path.Combine(Path.GetTempPath(), "Aiko.Specs", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("AIKO_USER_HOME", fakeHome);
        try
        {
            var adapter = new ClineAgentAdapter();
            await adapter.ApplyUserInstallAsync(CancellationToken.None);

            // A user-scope skill is visible in every folder, while the project is whichever folder the user
            // has open - so the global copy has to ask which one that is before it touches anything.
            var global = await File.ReadAllTextAsync(
                Path.Combine(fakeHome, ".agents", "skills", "aiko-create", "SKILL.md"));
            Assert.Contains("aiko project find", global, StringComparison.Ordinal);
            Assert.Contains("aiko init", global, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AIKO_USER_HOME", previousHome);
        }
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
                new UnifiedAgentInstaller([], context.Catalog, new FileProjectDefinitionStore(context.Catalog)),
                [],
                new DaemonEndpointConfiguration(dataPaths),
                new AccessTokenStore(dataPaths),
                context.Cards,
                new FileProjectDefinitionStore(context.Catalog),
                context.Executions);

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
    public async Task Doctor_reports_a_card_that_left_the_backlog_without_a_run()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var dataPaths = new AikoDataPaths(context.Database.DatabasePath);
            var doctor = new WorkshopDoctor(
                dataPaths,
                context.Catalog,
                new UnifiedAgentInstaller([], context.Catalog, new FileProjectDefinitionStore(context.Catalog)),
                [],
                new DaemonEndpointConfiguration(dataPaths),
                new AccessTokenStore(dataPaths),
                context.Cards,
                new FileProjectDefinitionStore(context.Catalog),
                context.Executions);

            // A card pushed into the pipeline with nothing running behind it. The store allows the move - the
            // board is the person's own - and what it leaves is a card that looks worked while its runs tab is
            // empty, which is the state nobody could see before this check.
            var created = CreateCard(context.Project.Id, "TASK-NO-RUN", 1);
            await context.Cards.SaveAsync(created, 0, CancellationToken.None);
            var pushed = created with
            {
                StageId = "implementation",
                Revision = 2
            };
            await context.Cards.SaveAsync(pushed, 1, CancellationToken.None);

            // A card worked one stage at a time: the analysis it left is completed, so it is not reported.
            var worked = CreateCard(context.Project.Id, "TASK-RUN", 1);
            await context.Cards.SaveAsync(worked, 0, CancellationToken.None);
            var analysis = await context.Executions.StartAsync(
                worked.Reference, "analysis", "claude-code", CancellationToken.None);
            await EstimateAsync(context, worked);
            await context.Executions.CompleteAsync(analysis.Id, [], [], CancellationToken.None);
            await context.Executions.StartAsync(
                worked.Reference, "implementation", "claude-code", CancellationToken.None);

            var report = await doctor.InspectAsync(context.Project.Id, CancellationToken.None);

            var finding = Assert.Single(report.Findings, item => item.Area == "card-progress");
            Assert.Equal(DiagnosticSeverity.Warning, finding.Severity);
            Assert.Contains("left the backlog", finding.Summary, StringComparison.Ordinal);
            Assert.Contains("TASK-NO-RUN", finding.Summary, StringComparison.Ordinal);
            Assert.DoesNotContain("TASK-RUN", finding.Summary, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Doctor_reports_a_card_that_moved_on_from_an_unfinished_stage()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var dataPaths = new AikoDataPaths(context.Database.DatabasePath);
            var doctor = new WorkshopDoctor(
                dataPaths,
                context.Catalog,
                new UnifiedAgentInstaller([], context.Catalog, new FileProjectDefinitionStore(context.Catalog)),
                [],
                new DaemonEndpointConfiguration(dataPaths),
                new AccessTokenStore(dataPaths),
                context.Cards,
                new FileProjectDefinitionStore(context.Catalog),
                context.Executions);

            // A card that started in a later stage without ever running the one before it: the leaving stage
            // was never started at all. Its run is completed so the project's one-run limit stays out of the way.
            var skipped = CreateCard(context.Project.Id, "TASK-NEVER-STARTED", 1);
            await context.Cards.SaveAsync(skipped, 0, CancellationToken.None);
            var skippedRun = await context.Executions.StartAsync(
                skipped.Reference, "implementation", "claude-code", CancellationToken.None);
            await EstimateAsync(context, skipped);
            await context.Executions.CompleteAsync(skippedRun.Id, [], [], CancellationToken.None);

            // A card whose leaving stage was completed is healthy and is not reported.
            var healthy = CreateCard(context.Project.Id, "TASK-HEALTHY", 1);
            await context.Cards.SaveAsync(healthy, 0, CancellationToken.None);
            var healthyAnalysis = await context.Executions.StartAsync(
                healthy.Reference, "analysis", "claude-code", CancellationToken.None);
            await EstimateAsync(context, healthy);
            await context.Executions.CompleteAsync(
                healthyAnalysis.Id, [], [], CancellationToken.None);
            var healthyImplementation = await context.Executions.StartAsync(
                healthy.Reference, "implementation", "claude-code", CancellationToken.None);
            await EstimateAsync(context, healthy);
            await context.Executions.CompleteAsync(
                healthyImplementation.Id, [], [], CancellationToken.None);

            // An analysis that was started and abandoned: the run is still open, and the card was pushed into
            // implementation anyway. There is an execution, so the first check cannot see it - only the state
            // of the stage behind the card says the work stopped half way. This run stays active, so the card
            // is set up last, after every other run was closed.
            var running = CreateCard(context.Project.Id, "TASK-STILL-RUNNING", 1);
            await context.Cards.SaveAsync(running, 0, CancellationToken.None);
            await context.Executions.StartAsync(
                running.Reference, "analysis", "claude-code", CancellationToken.None);
            // The start moved the card into analysis, so its revision is 2; the board-like push follows it.
            await context.Cards.SaveAsync(
                running with { StageId = "implementation", Revision = 3 }, 2, CancellationToken.None);

            var report = await doctor.InspectAsync(context.Project.Id, CancellationToken.None);

            var finding = Assert.Single(report.Findings, item => item.Area == "card-progress");
            Assert.Equal(DiagnosticSeverity.Warning, finding.Severity);
            Assert.Contains("not finished", finding.Summary, StringComparison.Ordinal);
            Assert.Contains("TASK-STILL-RUNNING", finding.Summary, StringComparison.Ordinal);
            Assert.Contains("'analysis', which was still running", finding.Summary, StringComparison.Ordinal);
            Assert.Contains("TASK-NEVER-STARTED", finding.Summary, StringComparison.Ordinal);
            Assert.Contains("'analysis', which was not started", finding.Summary, StringComparison.Ordinal);
            Assert.DoesNotContain("TASK-HEALTHY", finding.Summary, StringComparison.Ordinal);
        });
    }


    [Fact]
    public async Task Linked_projects_are_stored_with_what_they_are_for()
    {
        await WithInitializedProjectAsync(async context =>
        {
            // A neighbour to link: a registered project of its own, so the link has something to point at.
            var neighbourRoot = Path.GetFullPath(Path.Combine(context.ProjectRoot, "..", "neighbour"));
            Directory.CreateDirectory(neighbourRoot);
            var neighbour = await context.Initializer.InitializeAsync(
                new InitializeProjectRequest(
                    neighbourRoot,
                    "Neighbour",
                    ProjectGitPolicy.LocalOnly,
                    Slug: "neighbour"),
                CancellationToken.None);
            var links = new FileProjectLinkStore(context.Catalog);

            // Standing alone is the normal state: no file, nothing to read.
            Assert.Empty(await links.ListAsync(context.Project.Id, CancellationToken.None));

            // A link carries the neighbour's immutable id, the handle it is addressed by, and the sentence that
            // says what it is for - the sentence an agent reads before routing work there.
            const string why = "the desktop client - UI work is filed here";
            var saved = await links.SaveAsync(context.Project.Id, neighbour.Handle, why, CancellationToken.None);
            Assert.Equal(neighbour.Id, saved.ProjectId);
            Assert.Equal(neighbour.Handle, saved.Handle);
            Assert.Equal(why, saved.Description);

            // The registry is a file of the project, and its revision moves on every write.
            var linkPath = Path.Combine(context.StitchRoot, "links.json");
            Assert.True(File.Exists(linkPath));
            using var first = JsonDocument.Parse(await File.ReadAllTextAsync(linkPath));
            Assert.Equal(1, first.RootElement.GetProperty("revision").GetInt64());
            var stored = Assert.Single(await links.ListAsync(context.Project.Id, CancellationToken.None));
            Assert.Equal(why, stored.Description);

            // Linking the same project again replaces what the link says instead of filing a second one: a
            // registry with two rows about one neighbour is a registry nobody trusts.
            await links.SaveAsync(context.Project.Id, neighbour.Id, "still the desktop client", CancellationToken.None);
            Assert.Equal(
                "still the desktop client",
                Assert.Single(await links.ListAsync(context.Project.Id, CancellationToken.None)).Description);
            using var second = JsonDocument.Parse(await File.ReadAllTextAsync(linkPath));
            Assert.Equal(2, second.RootElement.GetProperty("revision").GetInt64());

            // A project nobody registered cannot be linked - the registry is what makes a link meaningful - and
            // neither can the project itself.
            await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
                await links.SaveAsync(context.Project.Id, "not-a-project", "anything", CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await links.SaveAsync(context.Project.Id, context.Project.Id, "anything", CancellationToken.None));

            // Removing takes the link out by either name, and removing it twice is not an error.
            await links.RemoveAsync(context.Project.Id, neighbour.Handle, CancellationToken.None);
            Assert.Empty(await links.ListAsync(context.Project.Id, CancellationToken.None));
            await links.RemoveAsync(context.Project.Id, neighbour.Id, CancellationToken.None);
        });
    }

    [Fact]
    public async Task Git_policy_reader_reads_the_manifest_and_answers_null_without_one()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var reader = new FileProjectGitPolicyReader();

            // The default template keeps Aiko's data local, and that is what the project's manifest says.
            Assert.Equal(
                ProjectGitPolicy.LocalOnly,
                await reader.ReadAsync(context.Project, CancellationToken.None));

            // A project whose manifest is not there has no policy to read: the answer is "unknown", because
            // guessing "local" or "tracked" is how a commit lands where it was not wanted.
            var moved = context.Project with { RootPath = Path.Combine(context.Project.RootPath, "gone") };
            Assert.Null(await reader.ReadAsync(moved, CancellationToken.None));
        });
    }

    [Fact]
    public async Task A_relation_saved_through_the_readable_handle_is_stored_under_the_project_id()
    {
        await WithInitializedProjectAsync(async context =>
        {
            // The setup only means something while the handle and the id differ: they do here, and an agent's
            // MCP endpoint carries the handle.
            Assert.NotEqual(context.Project.Id, context.Project.Handle);

            var parent = CreateCard(context.Project.Id, "TASK-PARENT", 1);
            var child = CreateCard(context.Project.Id, "TASK-CHILD", 1);
            await context.Cards.SaveAsync(parent, 0, CancellationToken.None);
            await context.Cards.SaveAsync(child, 0, CancellationToken.None);

            // A caller that addressed the project the way the endpoint does - by its readable handle. What lands
            // in relations.json must still be the immutable id: a handle there is a reference the reindexer
            // cannot resolve against the cards, and it refuses the whole project over that one edge.
            var relation = new CardRelation(
                Guid.CreateVersion7().ToString("N"),
                new CardReference(context.Project.Handle, parent.Reference.CardId),
                new CardReference(context.Project.Handle, child.Reference.CardId),
                RelationTypes.ParentChild,
                DateTimeOffset.UtcNow);
            await context.Relations.SaveAsync(relation, CancellationToken.None);

            var stored = Assert.Single(
                await context.Relations.ListAsync(context.Project.Id, CancellationToken.None));
            Assert.Equal(context.Project.Id, stored.Source.ProjectId);
            Assert.Equal(context.Project.Id, stored.Target.ProjectId);

            // The point of the fix: the projections can be rebuilt again.
            var reindexed = await context.Reindexer.ReindexAsync(
                context.Project.Id,
                CancellationToken.None);
            Assert.Equal(1, reindexed.Relations);
        });
    }

    [Fact]
    public async Task The_working_contract_requires_a_started_stage_before_files_change()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var installer = new UnifiedAgentInstaller(
                [new ClineAgentAdapter()],
                context.Catalog,
                new FileProjectDefinitionStore(context.Catalog));
            await installer.ApplyAsync(
                context.Project.Id,
                $"http://127.0.0.1:18471/mcp/projects/{context.Project.Handle}",
                "test-token",
                ["cline"],
                CancellationToken.None);

            // Work that starts with a card but never enters a stage leaves no execution, no artifacts and no
            // history: the card looks worked while its runs tab is empty. What an agent reads first - the
            // workspace rule and the run procedure - has to say where the work happens.
            var contract = await File.ReadAllTextAsync(
                Path.Combine(context.Project.RootPath, ".clinerules", "aiko.md"));
            Assert.Contains("aiko_start_stage", contract, StringComparison.Ordinal);
            Assert.Contains("aiko_complete_stage", contract, StringComparison.Ordinal);
            Assert.Contains("backlog", contract, StringComparison.Ordinal);

            // The owner's workflow, in the text an agent reads first: a request stops at the card, one run is
            // one stage, and --all is the named exception.
            Assert.Contains("and stop", contract, StringComparison.Ordinal);
            Assert.Contains("One run is one stage", contract, StringComparison.Ordinal);
            Assert.Contains("--all", contract, StringComparison.Ordinal);
            // --all is not only the named card's own pipeline: a container's pass creates its children and works
            // them. An epic run that reported the stories it would need and created none is what this closes.
            Assert.Contains("descends into the card's children", contract, StringComparison.Ordinal);
            // An order is still only a request. The incident this line closes was an agent that read "fix X" as
            // "run X" and did the work before the user had asked for it.
            Assert.Contains("An order is still a request", contract, StringComparison.Ordinal);

            var run = await File.ReadAllTextAsync(
                Path.Combine(context.Project.RootPath, ".cline", "skills", "aiko-run", "SKILL.md"));
            Assert.Contains("aiko_start_stage", run, StringComparison.Ordinal);
            Assert.Contains("Run ONE stage and stop", run, StringComparison.Ordinal);
            Assert.Contains("--all", run, StringComparison.Ordinal);
            // --all descends into the card's children. The whole tree keeps one run slot, so the parent's run has
            // to be parked before a child can start - that parking is what makes the descent possible at all, and
            // the procedure has to say both halves or the first child start would hit the run limit.
            Assert.Contains("descends into the card's children", run, StringComparison.Ordinal);
            Assert.Contains("aiko_pause_execution", run, StringComparison.Ordinal);
            Assert.Contains("aiko_resume_execution", run, StringComparison.Ordinal);
            Assert.Contains("maxConcurrentRuns", run, StringComparison.Ordinal);
            // The old claim - that moving the card is the only way it changes stage - was wrong and told an
            // agent to move a card it was about to start anyway.
            Assert.DoesNotContain("only way it changes stage", run, StringComparison.Ordinal);
            // The run procedure states the project's git rules before an agent gets a chance to push: Aiko has
            // no push of its own, so the rule is all there is.
            Assert.Contains("Do not push", run, StringComparison.Ordinal);

            // The card's feed is a notebook between stages: the contract and the run procedure both say to read it
            // before the work and to leave the outcome - signed with the agent's own id - before completing.
            Assert.Contains("notebook between stages", contract, StringComparison.Ordinal);
            Assert.Contains("aiko_list_comments", contract, StringComparison.Ordinal);
            Assert.Contains("Read the card's feed", run, StringComparison.Ordinal);
            Assert.Contains("adapter id", run, StringComparison.Ordinal);

            // A create procedure can be the only text an agent reads, so it says out loud that the card is the
            // whole answer: a procedure ending at "estimate it" reads as "now do the work".
            var create = await File.ReadAllTextAsync(
                Path.Combine(context.Project.RootPath, ".cline", "skills", "aiko-create", "SKILL.md"));
            Assert.Contains("Then stop. Creating the card", create, StringComparison.Ordinal);
            Assert.Contains("aiko-run <cardId>", create, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Settings_written_before_the_push_policy_read_as_deny()
    {
        await WithInitializedProjectAsync(async context =>
        {
            // A project configured before push had a policy: its document carries no such field. The effective
            // settings have to answer Deny rather than fail on a shape the daemon no longer writes.
            var path = Path.Combine(context.StitchRoot, "settings.json");
            await File.WriteAllTextAsync(
                path,
                """
                {
                  "schemaVersion": 1,
                  "execution": {
                    "workspaceMode": "Shared",
                    "maxConcurrentRuns": 1,
                    "scopeOverlapPolicy": "Ask",
                    "sharedCheckoutCommitPolicy": "Allow"
                  }
                }
                """);
            var settings = new AppSettingsService(new FileAppSettingsStore(context.Catalog));

            var effective = await settings.GetEffectiveExecutionAsync(
                context.Project.Id,
                CancellationToken.None);
            // What the file does state is read as it stands ...
            Assert.Equal(ActionPolicy.Allow, effective.SharedCheckoutCommitPolicy);
            // ... and what it does not state falls back to the safe answer.
            Assert.Equal(ActionPolicy.Deny, effective.SharedCheckoutPushPolicy);
        });
    }

    [Fact]
    public async Task A_card_keeps_both_of_its_texts_through_the_card_file()
    {
        await WithInitializedProjectAsync(async context =>
        {
            // The two texts are prose in the metadata, and the file is what a card is: if a key did not survive
            // the round-trip, the request would look unrecorded on a card that plainly has one.
            var card = CreateCard(context.Project.Id, "TASK-TEXTS", 1) with
            {
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [Card.RequestMetadataKey] = "the dropdown is empty on Fridays",
                    [Card.RequirementsMetadataKey] = "Make the dropdown list every value."
                }
            };
            await context.Cards.SaveAsync(card, 0, CancellationToken.None);

            var stored = await context.Cards.FindAsync(card.Reference, CancellationToken.None);
            Assert.Equal("the dropdown is empty on Fridays", stored?.Request);
            Assert.Equal("Make the dropdown list every value.", stored?.Requirements);
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
    public async Task A_card_type_carries_its_description_and_appearance()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var store = new FileProjectDefinitionStore(context.Catalog);
            var story = (await store.ReadAsync(context.Project.Id, CancellationToken.None))
                .Workflows.Single(item => item.Id == "story");

            // The type is the workflow, so what the project says about the type lives on the workflow, and
            // the icon and colour of a status column live on the stage.
            Assert.False(string.IsNullOrWhiteSpace(story.Description));
            Assert.Equal("account-tree", story.Icon);
            Assert.Equal("primary", story.Color);
            Assert.Equal("Story", story.CardType);
            Assert.All(story.Stages, stage => Assert.False(string.IsNullOrWhiteSpace(stage.Icon)));

            // A document written before the choice existed still loads: both fields are optional.
            var path = Path.Combine(context.StitchRoot, "workflows", "story.json");
            var document = System.Text.Json.Nodes.JsonNode.Parse(
                await File.ReadAllTextAsync(path))!.AsObject();
            document.Remove("icon");
            document.Remove("color");
            var stages = (System.Text.Json.Nodes.JsonArray)document["stages"]!;
            ((System.Text.Json.Nodes.JsonObject)stages[0]!).Remove("icon");
            await File.WriteAllTextAsync(path, document.ToJsonString());

            var legacy = (await store.ReadAsync(context.Project.Id, CancellationToken.None))
                .Workflows.Single(item => item.Id == "story");
            Assert.Null(legacy.Icon);
            Assert.Null(legacy.Stages[0].Icon);
        });
    }

    [Fact]
    public async Task Backlog_is_a_reserved_stage()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var store = new FileProjectDefinitionStore(context.Catalog);
            var workflow = (await store.ReadAsync(context.Project.Id, CancellationToken.None))
                .Workflows.Single(item => item.Id == "task");

            // Backlog is where a card enters the pipeline, so a workflow that drops it is refused rather
            // than saved into a state where a new card has nowhere to go.
            var withoutBacklog = workflow with
            {
                Stages = workflow.Stages.Where(stage => stage.Id != "backlog").ToArray(),
                Revision = workflow.Revision + 1
            };

            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await store.SaveWorkflowAsync(
                    context.Project.Id,
                    withoutBacklog,
                    workflow.Revision,
                    CancellationToken.None));

            // A pipeline that keeps the backlog but puts another stage ahead of it is refused too: the
            // backlog is where a card enters its workflow, so nothing may be placed before it.
            var beforeBacklog = workflow with
            {
                Stages = workflow.Stages
                    .Select(stage => stage.Id == "backlog"
                        ? stage with { Order = workflow.Stages.Max(item => item.Order) + 10 }
                        : stage)
                    .ToArray(),
                Revision = workflow.Revision + 1
            };

            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await store.SaveWorkflowAsync(
                    context.Project.Id,
                    beforeBacklog,
                    workflow.Revision,
                    CancellationToken.None));
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
                ["Task"],
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
                File.Exists(Path.Combine(context.StitchRoot, "workflows", "tasks", "outside.md")),
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
                "Task",
                "Реализация хранилища",
                "task",
                "implementation",
                1,
                10m,
                ["src/**"],
                [],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["requirements"] = "Сохранить карточку и показать её на доске"
                });

            await context.Cards.SaveAsync(firstRevision, 0, CancellationToken.None);

            var saved = await context.Cards.FindAsync(reference, CancellationToken.None);
            Assert.Equal(1L, saved?.Revision);
            var cardPath = Path.Combine(
                context.StitchRoot,
                "workflows",
                "tasks",
                reference.CardId,
                "card.json");
            Assert.True(File.Exists(cardPath), "card.json was not created");

            // A .aiko tree is read and diffed by people, so its text keeps its characters: the default
            // encoder would turn the title above into a wall of \u0440 escapes.
            var written = await File.ReadAllTextAsync(cardPath);
            Assert.Contains("Реализация хранилища", written, StringComparison.Ordinal);
            Assert.DoesNotContain("\\u04", written, StringComparison.Ordinal);

            var listed = await context.Cards.ListAsync(context.Project.Id, CancellationToken.None);
            Assert.Single(listed);
            Assert.Equal(reference.CardId, listed[0].Reference.CardId);

            // The board addresses a project by its readable handle, while the rows are keyed by its id:
            // listing by the handle has to find the same card, or the board answers with an empty list for
            // a project that plainly has cards.
            Assert.NotEqual(context.Project.Id, context.Project.Handle);
            var byHandle = await context.Cards.ListAsync(context.Project.Handle, CancellationToken.None);
            Assert.Single(byHandle);
            Assert.Equal(reference.CardId, byHandle[0].Reference.CardId);

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
    public async Task Project_settings_resolve_over_the_built_in_defaults()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var store = new FileAppSettingsStore(context.Catalog);
            var service = new AppSettingsService(store);

            // The project was created from the base template, so it already states its own complete snapshot:
            // what it runs with is its own document, and there is no level above it to fall back to.
            var created = await service.LoadAsync(context.Project.Id, CancellationToken.None);
            Assert.Equal(AppSettingsSources.Project, created.ExecutionSource);
            Assert.NotNull(created.Snapshot);
            Assert.True(File.Exists(Path.Combine(context.StitchRoot, "settings.json")));

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

            // With no project in hand the view answers with the built-in defaults, and says they are built in.
            var builtIn = await service.LoadAsync(null, CancellationToken.None);
            Assert.Equal(ExecutionSettings.SafeDefault, builtIn.EffectiveExecution);
            Assert.Equal(AppSettingsSources.Default, builtIn.ExecutionSource);

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
                "Story",
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
                "Task",
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
                await context.Relations.ListAsync(context.Project.Id, CancellationToken.None),
                Aiko.Domain.Prioritization.PrioritySettings.SafeDefault,
                [
                    new WorkflowDefinition("story", "Stories", [], 1),
                    new WorkflowDefinition("task", "Tasks", [], 1, BlendsWithParent: true)
                ]);

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
                Kind = "Story",
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
    public async Task An_execution_addressed_by_the_project_handle_is_stored_under_the_id()
    {
        await WithInitializedProjectAsync(async context =>
        {
            // An agent's MCP endpoint and the UI's URLs carry the readable handle, which is not the id the
            // projections are keyed by - and the executions table declares a foreign key against the id.
            Assert.NotEqual(context.Project.Id, context.Project.Handle);
            var card = CreateCard(context.Project.Id, "TASK-HANDLE", 1);
            await context.Cards.SaveAsync(card, 0, CancellationToken.None);

            var execution = await context.Executions.StartAsync(
                new CardReference(context.Project.Handle, "TASK-HANDLE"),
                "implementation",
                "claude-code",
                CancellationToken.None);

            // The row exists at all: this is the insert that used to fail with
            // "SQLite Error 19: 'FOREIGN KEY constraint failed'" because the handle went in unresolved.
            Assert.Equal(context.Project.Id, execution.Card.ProjectId);
            Assert.Equal(
                execution.Id,
                Assert.Single(await context.Executions.ListAsync(
                    new CardReference(context.Project.Id, "TASK-HANDLE"),
                    CancellationToken.None)).Id);

            // The card's "runs" tab asks through the handle, and it must answer with the run rather than
            // coming back empty.
            Assert.Equal(
                execution.Id,
                Assert.Single(await context.Executions.ListAsync(
                    new CardReference(context.Project.Handle, "TASK-HANDLE"),
                    CancellationToken.None)).Id);

            // The journal is keyed the same way, so a subscriber of the project receives the change.
            var events = await context.EventJournal.ReadAsync(
                context.Project.Id, afterId: 0, limit: 50, CancellationToken.None);
            Assert.Contains(events, item =>
                item.Type == AikoEventTypes.ExecutionUpdated &&
                item.PayloadJson.Contains("TASK-HANDLE", StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task Project_activity_counts_only_the_days_of_that_project()
    {
        await WithInitializedProjectAsync(async context =>
        {
            // A second project in the same installation: the two share the daemon and its database, which
            // is exactly what the project filter has to separate.
            var otherRoot = Path.Combine(context.ProjectRoot, "..", "Other");
            Directory.CreateDirectory(otherRoot);
            var other = await context.Initializer.InitializeAsync(
                new InitializeProjectRequest(otherRoot),
                CancellationToken.None);

            var report = new SqliteActivityReport(context.Database, context.Catalog);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            // Addressed by the readable handle, the way the project page addresses it.
            var before = await report.GetProjectActivityAsync(
                context.Project.Handle, 7, CancellationToken.None);
            var otherBefore = await report.GetProjectActivityAsync(
                other.Handle, 7, CancellationToken.None);

            var card = CreateCard(other.Id, "TASK-ACTIVITY-OTHER", 1);
            await context.Cards.SaveAsync(card, 0, CancellationToken.None);

            var after = await report.GetProjectActivityAsync(
                context.Project.Handle, 7, CancellationToken.None);
            var otherAfter = await report.GetProjectActivityAsync(
                other.Handle, 7, CancellationToken.None);

            // Work in one project is not this project's activity.
            Assert.Equal(ActivityOn(before, today), ActivityOn(after, today));
            Assert.True(
                ActivityOn(otherAfter, today) > ActivityOn(otherBefore, today),
                "the other project's own day did not grow");

            // The installation-wide series still sees everything, which is what makes the narrow one mean
            // something.
            var everything = await report.GetActivityAsync(7, CancellationToken.None);
            Assert.True(ActivityOn(everything, today) >= ActivityOn(otherAfter, today));

            // An unknown project is an error rather than an empty calendar.
            await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
                await report.GetProjectActivityAsync(Guid.NewGuid().ToString("N"), 7, CancellationToken.None));
        });

        static int ActivityOn(IReadOnlyList<ActivityDay> days, DateOnly day) =>
            days.Where(item => item.Date == day).Sum(item => item.Count);
    }

    [Fact]
    public async Task Analytics_read_by_the_handle_fill_the_distribution()
    {
        await WithInitializedProjectAsync(async context =>
        {
            // The card page and the project page address a project by its handle, and the cards projection
            // is keyed by the id - so reading the charts through the handle is the case that used to come
            // back with an empty distribution.
            Assert.NotEqual(context.Project.Id, context.Project.Handle);
            var story = CreateCard(context.Project.Id, "STORY-DISTRIBUTION", 1) with
            {
                Kind = "Story",
                WorkflowId = "story",
                Size = "M"
            };
            var task = CreateCard(context.Project.Id, "TASK-DISTRIBUTION", 1) with { Size = "XS" };
            await context.Cards.SaveAsync(story, 0, CancellationToken.None);
            await context.Cards.SaveAsync(task, 0, CancellationToken.None);

            var analytics = await new SqliteProjectAnalytics(context.Database, context.Catalog).ReadAsync(
                context.Project.Handle, 8, CancellationToken.None);

            Assert.Equal(2, analytics.ByKind.Sum(bucket => bucket.Count));
            Assert.Contains(analytics.ByKind, bucket => StringComparer.Ordinal.Equals(bucket.Label, "Story"));
            Assert.Contains(analytics.ByKind, bucket => StringComparer.Ordinal.Equals(bucket.Label, "Task"));
            Assert.Contains(analytics.BySize, bucket => StringComparer.Ordinal.Equals(bucket.Label, "M"));
            Assert.Contains(analytics.BySize, bucket => StringComparer.Ordinal.Equals(bucket.Label, "XS"));
        });
    }

    [Fact]
    public async Task Project_procedures_are_skills_named_after_the_directory_they_live_in()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var installer = new UnifiedAgentInstaller(
                [new ClineAgentAdapter()],
                context.Catalog,
                new FileProjectDefinitionStore(context.Catalog));
            await installer.ApplyAsync(
                context.Project.Id,
                $"http://127.0.0.1:18471/mcp/projects/{context.Project.Id}",
                "test-token",
                ["cline"],
                CancellationToken.None);

            var skillsRoot = Path.Combine(context.Project.RootPath, ".cline", "skills");
            var directories = Directory.EnumerateDirectories(skillsRoot).ToArray();
            Assert.NotEmpty(directories);
            foreach (var directory in directories)
            {
                var name = Path.GetFileName(directory);
                var lines = await File.ReadAllLinesAsync(Path.Combine(directory, "SKILL.md"));
                // A client matches a skill by the pair it reads first: the name, which has to equal the
                // directory the skill lives in...
                Assert.Equal("---", lines[0]);
                Assert.Equal($"name: {name}", lines[1]);
                // ...and a description the frontmatter can carry as a plain scalar, so a colon followed by
                // a space - which would read as another mapping key - is not allowed in it.
                Assert.StartsWith("description: ", lines[2], StringComparison.Ordinal);
                Assert.DoesNotContain(": ", lines[2]["description: ".Length..], StringComparison.Ordinal);
                Assert.Equal("---", lines[3]);
                Assert.Contains("aiko_", string.Join('\n', lines), StringComparison.Ordinal);
            }
        });
    }

    [Fact]
    public async Task A_skill_of_a_previous_release_leaves_no_empty_directory_behind()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var installer = new UnifiedAgentInstaller(
                [new ClineAgentAdapter()],
                context.Catalog,
                new FileProjectDefinitionStore(context.Catalog));
            var skillsRoot = Path.Combine(context.Project.RootPath, ".cline", "skills");
            // What an earlier Aiko wrote: the working contract as a skill of its own.
            var stale = Path.Combine(skillsRoot, "aiko-project");
            Directory.CreateDirectory(stale);
            await File.WriteAllTextAsync(
                Path.Combine(stale, "SKILL.md"), "<!-- Managed by Aiko -->\nname: aiko-project");
            // A skill the user wrote is not Aiko's to remove, marker or not.
            var mine = Path.Combine(skillsRoot, "my-skill");
            Directory.CreateDirectory(mine);
            await File.WriteAllTextAsync(Path.Combine(mine, "SKILL.md"), "my own skill");

            await installer.ApplyAsync(
                context.Project.Id,
                $"http://127.0.0.1:18471/mcp/projects/{context.Project.Id}",
                "test-token",
                ["cline"],
                CancellationToken.None);

            // The stale skill goes, and its directory with it, because nothing else lives there...
            Assert.False(File.Exists(Path.Combine(stale, "SKILL.md")));
            Assert.False(Directory.Exists(stale));
            // ...while a directory that still holds anything keeps it, and the managed root stays.
            Assert.True(File.Exists(Path.Combine(mine, "SKILL.md")));
            Assert.True(Directory.Exists(mine));
            Assert.True(Directory.Exists(skillsRoot));
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
            // The init command names the adapter it was generated for, so a project created from inside that
            // agent is connected to it in the same step instead of the user having to say who they are.
            Assert.Contains(
                "--agent claude-code",
                await File.ReadAllTextAsync(Path.Combine(fakeHome, ".claude", "commands", "aiko-init.md")),
                StringComparison.Ordinal);

            // The settings command is installed with the rest, and it keeps the two targets it writes apart:
            // a project's own document and a template's defaults. A skill that blurred them would send the
            // user to edit a template believing they were changing a project.
            var settings = await File.ReadAllTextAsync(
                Path.Combine(fakeHome, ".claude", "commands", "aiko-settings.md"));
            Assert.Contains("aiko_update_settings", settings, StringComparison.Ordinal);
            Assert.Contains("never changes a project", settings, StringComparison.Ordinal);

            var removed = await adapter.UninstallUserAsync(CancellationToken.None);
            Assert.True(removed.Succeeded);
            Assert.False(File.Exists(Path.Combine(fakeHome, ".claude", "skills", "aiko", "SKILL.md")));
            Assert.False(File.Exists(Path.Combine(fakeHome, ".claude", "commands", "aiko-init.md")));
            Assert.False(File.Exists(Path.Combine(fakeHome, ".claude", "commands", "aiko-settings.md")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("AIKO_USER_HOME", previousHome);
        }
    }

    [Fact]
    public async Task A_project_reports_which_agents_are_connected_to_it()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var installer = new UnifiedAgentInstaller(
                [new CodexAgentAdapter(), new ClineAgentAdapter()],
                context.Catalog,
                new FileProjectDefinitionStore(context.Catalog));

            // A fresh project has nothing for either adapter. That is a different fact from whether the agents
            // are installed on the machine, and it has to be asked of the project.
            var before = await installer.ReadProjectConnectionsAsync(context.Project.Id, CancellationToken.None);
            Assert.Equal(2, before.Count);
            Assert.All(before, connection => Assert.False(connection.Connected));

            await installer.ApplyAsync(
                context.Project.Id,
                $"http://127.0.0.1:18471/mcp/projects/{context.Project.Handle}",
                "test-token",
                ["cline"],
                CancellationToken.None);

            var after = await installer.ReadProjectConnectionsAsync(context.Project.Id, CancellationToken.None);
            var cline = Assert.Single(after, connection => connection.AdapterId == "cline");
            Assert.True(cline.Connected);
            Assert.Equal("Cline", cline.DisplayName);
            Assert.False(Assert.Single(after, connection => connection.AdapterId == "codex").Connected);

            // And the answer follows the disk: a file deleted by hand is not reported as connected, because
            // nothing was recorded that could go stale.
            Directory.Delete(Path.Combine(context.Project.RootPath, ".cline"), true);
            var swept = await installer.ReadProjectConnectionsAsync(context.Project.Id, CancellationToken.None);
            Assert.False(Assert.Single(swept, connection => connection.AdapterId == "cline").Connected);
        });
    }

    [Fact]
    public async Task Cline_user_scope_installs_the_generic_procedures_in_the_portable_root()
    {
        var previousHome = Environment.GetEnvironmentVariable("AIKO_USER_HOME");
        var fakeHome = Path.Combine(Path.GetTempPath(), "Aiko.Specs", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("AIKO_USER_HOME", fakeHome);
        try
        {
            var adapter = new ClineAgentAdapter();
            var applied = await adapter.ApplyUserInstallAsync(CancellationToken.None);
            Assert.True(applied.Succeeded);

            // A Cline build that does not surface workspace skills reads the global root and nothing else,
            // so a procedure left in <project>/.cline/skills is a procedure the user cannot reach at all.
            var run = Path.Combine(fakeHome, ".agents", "skills", "aiko-run", "SKILL.md");
            Assert.True(File.Exists(run));
            var runText = await File.ReadAllTextAsync(run);
            Assert.Contains("name: aiko-run", runText, StringComparison.Ordinal);
            // The adapter that starts a stage is the one the file was installed for.
            Assert.Contains("\"cline\"", runText, StringComparison.Ordinal);

            // Every procedure that works on whichever project is open is installed globally...
            foreach (var name in new[]
                     {
                         "aiko-create", "aiko-create-sub", "aiko-estimate", "aiko-scope",
                         "aiko-handoff", "aiko-memory", "aiko-status", "aiko-ui"
                     })
            {
                Assert.True(File.Exists(Path.Combine(fakeHome, ".agents", "skills", name, "SKILL.md")));
            }

            // ...and nothing that names one project's card type, because a type is project data. The
            // type-agnostic aiko-create is what covers those.
            Assert.False(Directory.Exists(Path.Combine(fakeHome, ".agents", "skills", "aiko-create-story")));

            // The contract also goes into Cline's own rules root, not only the workspace: the app reads global
            // rules in every folder, and a workspace rule alone reaches nobody in a build that does not surface
            // workspace rules - the same gap the skills have.
            var globalRule = Path.Combine(fakeHome, ".cline", "rules", "aiko.md");
            Assert.True(File.Exists(globalRule));
            Assert.Contains(
                "starts with a card",
                await File.ReadAllTextAsync(globalRule),
                StringComparison.Ordinal);

            var removed = await adapter.UninstallUserAsync(CancellationToken.None);
            Assert.True(removed.Succeeded);
            Assert.False(File.Exists(run));
            Assert.False(File.Exists(globalRule));
            // Cline's own skills root is emptied as well: both halves are this adapter's.
            Assert.False(File.Exists(Path.Combine(fakeHome, ".cline", "skills", "aiko", "SKILL.md")));
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
    public async Task Project_initialization_writes_a_complete_settings_snapshot()
    {
        await WithInitializedProjectAsync(async context =>
        {
            // The base template states no settings, so a project created from it takes the built-in values -
            // and states them, which is what keeps a later release from changing this project's behaviour.
            var newRoot = Path.Combine(context.ProjectRoot, "new-project");
            Directory.CreateDirectory(newRoot);
            var newProject = await context.Initializer.InitializeAsync(
                new InitializeProjectRequest(newRoot),
                CancellationToken.None);

            var service = new AppSettingsService(new FileAppSettingsStore(context.Catalog));
            var view = await service.LoadAsync(newProject.Id, CancellationToken.None);

            Assert.Equal(AppSettingsSources.Project, view.ExecutionSource);
            Assert.Equal(ExecutionSettings.SafeDefault, view.EffectiveExecution);
            Assert.Equal(
                Aiko.Domain.Prioritization.PrioritySettings.SafeDefault.Grid.Count,
                view.EffectivePriority.Grid.Count);
            Assert.True(File.Exists(Path.Combine(newRoot, ".aiko", "settings.json")));
        });
    }

    [Fact]
    public async Task Project_priority_weights_override_the_default()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var store = new FileAppSettingsStore(context.Catalog);
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
            var settings = new AppSettingsService(new FileAppSettingsStore(context.Catalog));
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
            var settings = new AppSettingsService(new FileAppSettingsStore(context.Catalog));
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
            var settings = new AppSettingsService(new FileAppSettingsStore(context.Catalog));
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

    /// <summary>
    /// Only a working agent occupies a concurrency slot: a run that waits for the user, is paused or needs
    /// attention is not doing anything, so it must not keep the project's limit exhausted (TASK-89).
    /// </summary>
    [Fact]
    public async Task Only_a_running_execution_holds_a_concurrency_slot()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var settings = new AppSettingsService(new FileAppSettingsStore(context.Catalog));
            await settings.SaveProjectAsync(
                context.Project.Id,
                new AppSettings(
                    AppSettings.CurrentSchemaVersion,
                    new ExecutionSettings(WorkspaceMode.Shared, 1, ActionPolicy.Ask, ActionPolicy.Deny)),
                CancellationToken.None);
            var executions = new SqliteExecutionCoordinator(
                context.Catalog, context.Cards, context.Database, settings);

            // Each state where no agent works is checked against the same slot, so one round each.
            foreach (var state in new[]
            {
                AgentAttemptState.WaitingForUser,
                AgentAttemptState.Paused,
                AgentAttemptState.RateLimited,
                AgentAttemptState.Failed
            })
            {
                var holder = CreateCard(context.Project.Id, $"TASK-SLOT-A-{state}", 1);
                var waiter = CreateCard(context.Project.Id, $"TASK-SLOT-B-{state}", 1);
                await context.Cards.SaveAsync(holder, 0, CancellationToken.None);
                await context.Cards.SaveAsync(waiter, 0, CancellationToken.None);

                var started = await executions.StartAsync(
                    holder.Reference, "implementation", "cline", CancellationToken.None);
                Assert.Equal(StageExecutionState.Running, started.State);

                // A working run holds the only slot, so the second card waits.
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await executions.StartAsync(
                        waiter.Reference, "implementation", "cline", CancellationToken.None));

                var parked = await executions.ReportAgentStateAsync(
                    started.Id, state, "no agent is working here", CancellationToken.None);
                Assert.NotEqual(StageExecutionState.Running, parked.State);

                // Parked, the first run stops counting, so the second card starts.
                var accepted = await executions.StartAsync(
                    waiter.Reference, "implementation", "cline", CancellationToken.None);
                Assert.Equal(StageExecutionState.Running, accepted.State);

                // Release the slot for the next round without walking the card's stages.
                await executions.ReportAgentStateAsync(
                    accepted.Id, AgentAttemptState.Cancelled, "released", CancellationToken.None);
            }
        });
    }

    /// <summary>
    /// A parked run is not writing files while it waits, so it must not block a start through scope overlap
    /// either (TASK-89): the same declared scope conflicts while the run works and stops conflicting once it waits.
    /// </summary>
    [Fact]
    public async Task A_parked_execution_does_not_block_a_start_by_scope_overlap()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var settings = new AppSettingsService(new FileAppSettingsStore(context.Catalog));
            await settings.SaveProjectAsync(
                context.Project.Id,
                new AppSettings(
                    AppSettings.CurrentSchemaVersion,
                    new ExecutionSettings(WorkspaceMode.Shared, 2, ActionPolicy.Ask, ActionPolicy.Deny)),
                CancellationToken.None);
            var executions = new SqliteExecutionCoordinator(
                context.Catalog, context.Cards, context.Database, settings);

            var first = CreateCard(context.Project.Id, "TASK-PARK-A", 1) with { DeclaredScopeFiles = ["src/**"] };
            var second = CreateCard(context.Project.Id, "TASK-PARK-B", 1) with { DeclaredScopeFiles = ["src/Foo/**"] };
            await context.Cards.SaveAsync(first, 0, CancellationToken.None);
            await context.Cards.SaveAsync(second, 0, CancellationToken.None);

            var started = await executions.StartAsync(
                first.Reference, "implementation", "cline", CancellationToken.None);

            // While the first run works, the overlap is real and the policy asks.
            var denied = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await executions.StartAsync(
                    second.Reference, "implementation", "cline", CancellationToken.None));
            Assert.Contains("scope", denied.Message, StringComparison.OrdinalIgnoreCase);

            // Parked, it holds nothing back and the same start goes through.
            await executions.PauseAsync(started.Id, "waiting for the user", CancellationToken.None);
            var accepted = await executions.StartAsync(
                second.Reference, "implementation", "cline", CancellationToken.None);
            Assert.Equal(StageExecutionState.Running, accepted.State);
        });
    }

    [Fact]
    public async Task Board_priority_propagates_through_multiple_parent_levels()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var story = new Card(
                new CardReference(context.Project.Id, "STORY-DEEP"),
                "Story", "story", "story", "backlog", 1, 10m, [], [], new Dictionary<string, string>());
            var task = new Card(
                new CardReference(context.Project.Id, "TASK-DEEP"),
                "Task", "task", "task", "backlog", 1, 4m, [], [], new Dictionary<string, string>());
            var subtask = new Card(
                new CardReference(context.Project.Id, "SUB-DEEP"),
                "Task", "subtask", "task", "backlog", 1, 1m, [], [], new Dictionary<string, string>());
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
                await context.Relations.ListAsync(context.Project.Id, CancellationToken.None),
                Aiko.Domain.Prioritization.PrioritySettings.SafeDefault,
                [
                    new WorkflowDefinition("story", "Stories", [], 1),
                    new WorkflowDefinition("task", "Tasks", [], 1, BlendsWithParent: true)
                ]);

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
            var stories = Path.Combine(context.StitchRoot, "workflows", "stories", "DUP-1");
            var tasks = Path.Combine(context.StitchRoot, "workflows", "tasks", "DUP-1");
            Directory.CreateDirectory(stories);
            Directory.CreateDirectory(tasks);
            var story = CreateCard(context.Project.Id, "DUP-1", 1) with { Kind = "Story" };
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
    public async Task Starting_the_stage_a_card_is_working_in_continues_it()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var card = CreateCard(context.Project.Id, "TASK-CONTINUE", 1);
            await context.Cards.SaveAsync(card, 0, CancellationToken.None);

            var first = await context.Executions.StartAsync(
                card.Reference, "implementation", "claude-code", CancellationToken.None);

            // The card is pulled back while its stage stays open, then the same stage is started again: the run
            // is continued rather than opened twice, the card comes back to where the work is, and the run limit
            // is not consulted - this is the same run, not a second one.
            var pulledBack = await context.Cards.FindAsync(card.Reference, CancellationToken.None)
                ?? throw new InvalidOperationException("The card disappeared mid-spec.");
            await context.Cards.SaveAsync(
                pulledBack with { StageId = "backlog", Revision = pulledBack.Revision + 1 },
                pulledBack.Revision,
                CancellationToken.None);
            var second = await context.Executions.StartAsync(
                card.Reference, "implementation", "codex", CancellationToken.None);

            Assert.Equal(first.Id, second.Id);
            Assert.Equal(StageExecutionState.Running, second.State);
            Assert.Equal(2, second.Attempts.Count);
            Assert.Equal(AgentAttemptState.Superseded, second.Attempts[0].State);
            Assert.Equal("codex", second.Attempts[^1].AgentAdapterId);
            Assert.Equal(
                "implementation",
                (await context.Cards.FindAsync(card.Reference, CancellationToken.None))!.StageId);
            Assert.Single(await context.Executions.ListAsync(card.Reference, CancellationToken.None));
        });
    }

    [Fact]
    public async Task Starting_another_stage_while_one_is_unfinished_is_refused()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var card = CreateCard(context.Project.Id, "TASK-UNFINISHED", 1);
            await context.Cards.SaveAsync(card, 0, CancellationToken.None);
            var started = await context.Executions.StartAsync(
                card.Reference, "analysis", "claude-code", CancellationToken.None);
            await context.Executions.PauseAsync(
                started.Id, "Waiting for the user.", CancellationToken.None);

            var refused = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await context.Executions.StartAsync(
                    card.Reference, "implementation", "claude-code", CancellationToken.None));

            // The refusal names the stage, the state it stopped in and what to do: an agent has to be able to
            // act on it without reading the daemon's log.
            Assert.Contains("analysis", refused.Message, StringComparison.Ordinal);
            Assert.Contains("Paused", refused.Message, StringComparison.Ordinal);
            Assert.Contains("aiko_complete_stage", refused.Message, StringComparison.Ordinal);
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
            // A second run of the card is refused while this one is open. The same stage would now be continued
            // (TASK-16), so the refusal this spec guards is the other one: another stage has to wait until this
            // stage is finished.
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await context.Executions.StartAsync(
                    card.Reference,
                    "review",
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

            await EstimateAsync(context, card);
            var completed = await context.Executions.CompleteAsync(
                started.Id,
                ["src/Storage.cs", "docs/storage.md"],
                ["implementation.md"],
                CancellationToken.None);
            Assert.Equal(StageExecutionState.Completed, completed.State);
            var outOfScope = Assert.Single(completed.OutOfScopeFiles);
            Assert.Equal("docs/storage.md", outOfScope);

            var savedCard = await context.Cards.FindAsync(card.Reference, CancellationToken.None);
            Assert.Equal(4L, savedCard?.Revision);
            Assert.Equal(2, savedCard?.ActualChangedFiles.Count);

            var handoffDirectory = Path.Combine(
                context.StitchRoot,
                "workflows",
                "tasks",
                card.Reference.CardId,
                "handoffs");
            Assert.Single(Directory.GetFiles(handoffDirectory, "*.md"));
        });
    }
    [Fact]
    public async Task Completing_a_stage_requires_a_card_that_was_re_estimated_in_the_run()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var card = CreateCard(context.Project.Id, "TASK-ESTIMATE-GATE", 1);
            await context.Cards.SaveAsync(card, 0, CancellationToken.None);
            var started = await context.Executions.StartAsync(
                card.Reference, "implementation", "claude-code", CancellationToken.None);

            // The card still carries the estimate it was created with - nothing refreshed it during this run, so
            // completing the stage would record a readiness that describes the card as it was before the work.
            var refused = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await context.Executions.CompleteAsync(
                    started.Id, ["src/Storage.cs"], [], CancellationToken.None));
            Assert.Contains("aiko_estimate_card", refused.Message, StringComparison.Ordinal);
            Assert.Contains("readiness", refused.Message, StringComparison.Ordinal);
            Assert.Contains("TASK-ESTIMATE-GATE", refused.Message, StringComparison.Ordinal);

            // The refusal is a gate, not a cancellation: the run is still open and can be finished.
            var stillOpen = await context.Executions.FindAsync(started.Id, CancellationToken.None);
            Assert.Equal(StageExecutionState.Running, stillOpen?.State);

            await EstimateAsync(context, card);
            var completed = await context.Executions.CompleteAsync(
                started.Id, ["src/Storage.cs"], [], CancellationToken.None);
            Assert.Equal(StageExecutionState.Completed, completed.State);
        });
    }



    [Fact]
    public async Task An_agent_whose_application_leaves_a_data_directory_is_detected_without_an_executable()
    {
        var root = Path.Combine(Path.GetTempPath(), "Aiko.Specs", Guid.NewGuid().ToString("N"));
        var home = Path.Combine(root, "home");
        var emptyPath = Path.Combine(root, "empty");
        Directory.CreateDirectory(emptyPath);
        // ZCode ships without an executable of its own name on PATH, so its data directory is the only thing
        // that says it is installed - the discovery Cline has always used, now shared by every adapter.
        Directory.CreateDirectory(Path.Combine(home, ".zcode"));

        var previousPath = Environment.GetEnvironmentVariable("PATH");
        var previousHome = Environment.GetEnvironmentVariable("AIKO_USER_HOME");
        try
        {
            Environment.SetEnvironmentVariable("PATH", emptyPath);
            Environment.SetEnvironmentVariable("AIKO_USER_HOME", home);

            var installations = await new ZCodeAgentAdapter().DetectInstallationsAsync(CancellationToken.None);

            var installation = Assert.Single(installations);
            Assert.Equal(Path.Combine(home, ".zcode"), installation.ExecutablePath);
            Assert.Null(installation.Version);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", previousPath);
            Environment.SetEnvironmentVariable("AIKO_USER_HOME", previousHome);
            Directory.Delete(root, true);
        }
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
        var previousHome = Environment.GetEnvironmentVariable("AIKO_USER_HOME");
        try
        {
            // PATH is replaced rather than prepended, so the result does not depend on whether the machine
            // running the tests happens to have a real zcode installed, and the home directory is isolated for
            // the same reason: a data directory left by a real install is another signal now.
            Environment.SetEnvironmentVariable("PATH", directory);
            Environment.SetEnvironmentVariable("AIKO_USER_HOME", Path.Combine(directory, "home"));
            var installations = await new ZCodeAgentAdapter().DetectInstallationsAsync(CancellationToken.None);

            var installation = Assert.Single(installations);
            Assert.Equal(Path.Combine(directory, "zcode.cmd"), installation.ExecutablePath);
            // Discovery scans PATH; it does not run the agent, so the version stays unknown.
            Assert.Null(installation.Version);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            Environment.SetEnvironmentVariable("AIKO_USER_HOME", previousHome);
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Card_types_drive_the_generated_create_commands()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var installer = new UnifiedAgentInstaller(
                [new ClaudeCodeAgentAdapter()],
                context.Catalog,
                new FileProjectDefinitionStore(context.Catalog));
            var endpoint = $"http://127.0.0.1:18471/mcp/projects/{context.Project.Id}";
            var commands = Path.Combine(context.Project.RootPath, ".claude", "commands");

            await installer.ApplyAsync(
                context.Project.Id, endpoint, "test-token", ["claude-code"], CancellationToken.None);

            // One type-agnostic command plus one command per type the project declares, one command that
            // creates a sub-card under an existing card, and one that estimates a card on its own.
            Assert.True(File.Exists(Path.Combine(commands, "aiko-create.md")));
            Assert.True(File.Exists(Path.Combine(commands, "aiko-create-story.md")));
            Assert.True(File.Exists(Path.Combine(commands, "aiko-create-task.md")));
            Assert.True(File.Exists(Path.Combine(commands, "aiko-create-sub.md")));
            Assert.True(File.Exists(Path.Combine(commands, "aiko-estimate.md")));
            var story = await File.ReadAllTextAsync(Path.Combine(commands, "aiko-create-story.md"));
            Assert.Contains("kind=Story", story, StringComparison.Ordinal);
            // A created card is named and staged by Aiko, so the command must not send an id or a stage...
            Assert.Contains("backlog", story, StringComparison.Ordinal);
            Assert.DoesNotContain("workflowId=", story, StringComparison.Ordinal);
            Assert.DoesNotContain("stageId=", story, StringComparison.Ordinal);
            // ...but it does estimate the card in the same pass, so a new card is never left unranked.
            Assert.Contains("aiko_estimate_card", story, StringComparison.Ordinal);

            // A sub-card is linked from its parent: the edge the board reads as parent to child.
            var sub = await File.ReadAllTextAsync(Path.Combine(commands, "aiko-create-sub.md"));
            Assert.Contains("parent-child", sub, StringComparison.Ordinal);
            Assert.Contains("sourceCardId", sub, StringComparison.Ordinal);
            Assert.Contains("targetCardId", sub, StringComparison.Ordinal);

            // The same procedures are installed as skills as well: that is the channel a model loads by
            // relevance, while the command is the one a person types.
            var skills = Path.Combine(context.Project.RootPath, ".claude", "skills");
            Assert.Contains(
                "name: aiko-run",
                await File.ReadAllTextAsync(Path.Combine(skills, "aiko-run", "SKILL.md")),
                StringComparison.Ordinal);
            Assert.Contains(
                "name: aiko-create-task",
                await File.ReadAllTextAsync(Path.Combine(skills, "aiko-create-task", "SKILL.md")),
                StringComparison.Ordinal);
            // The working contract is not one of them: it sits in CLAUDE.md, the file Claude Code reads
            // whether or not a model judges anything relevant.
            var claudeMemory = await File.ReadAllTextAsync(
                Path.Combine(context.Project.RootPath, "CLAUDE.md"));
            Assert.Contains("aiko:begin", claudeMemory, StringComparison.Ordinal);
            Assert.Contains("durable project workflow and task memory", claudeMemory, StringComparison.Ordinal);

            // Commands an older Aiko wrote are no longer part of the plan, so the next install sweeps them
            // instead of leaving two ways to do the same thing.
            var legacy = Path.Combine(commands, "aiko-story-create.md");
            var perStage = Path.Combine(commands, "aiko-analyze.md");
            await File.WriteAllTextAsync(legacy, "<!-- Managed by Aiko -->\nlegacy");
            await File.WriteAllTextAsync(perStage, "<!-- Managed by Aiko -->\nper stage");
            await installer.ApplyAsync(
                context.Project.Id, endpoint, "test-token", ["claude-code"], CancellationToken.None);
            Assert.False(File.Exists(legacy));
            Assert.False(File.Exists(perStage));

            // One command runs a card whatever its pipeline says, and it names the adapter it was installed
            // for so aiko_start_stage records the right one.
            var run = await File.ReadAllTextAsync(Path.Combine(commands, "aiko-run.md"));
            Assert.Contains("aiko_start_stage", run, StringComparison.Ordinal);
            Assert.Contains("claude-code", run, StringComparison.Ordinal);

            // A type the project adds gets a command of its own.
            var store = new FileProjectDefinitionStore(context.Catalog);
            await store.CreateWorkflowAsync(
                context.Project.Id,
                new WorkflowDefinition(
                    "bug",
                    "Bugs",
                    [
                        new StageDefinition(
                            "backlog", "Backlog", 10, "Clarify the bug.", ["Bug"], null, [],
                            new Dictionary<string, ActionPolicy>(StringComparer.Ordinal)),
                        new StageDefinition(
                            "done", "Done", 20, "Record the fix.", ["Bug"], null, [],
                            new Dictionary<string, ActionPolicy>(StringComparer.Ordinal))
                    ],
                    1,
                    "Something that does not work."),
                CancellationToken.None);

            await installer.ReprojectCardTypesAsync(
                context.Project.Id, endpoint, "test-token", CancellationToken.None);
            var bug = Path.Combine(commands, "aiko-create-bug.md");
            Assert.True(File.Exists(bug));
            var bugCommand = await File.ReadAllTextAsync(bug);
            Assert.Contains("kind=Bug", bugCommand, StringComparison.Ordinal);
            Assert.Contains("Something that does not work.", bugCommand, StringComparison.Ordinal);

            // Removing the type removes its command, and leaves the other types alone.
            await store.DeleteWorkflowAsync(context.Project.Id, "bug", CancellationToken.None);
            await installer.ReprojectCardTypesAsync(
                context.Project.Id, endpoint, "test-token", CancellationToken.None);
            Assert.False(File.Exists(bug));
            Assert.True(File.Exists(Path.Combine(commands, "aiko-create-task.md")));
        });
    }

    [Fact]
    public async Task Card_type_commands_are_not_written_into_a_project_that_never_connected_an_agent()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var installer = new UnifiedAgentInstaller(
                [new ClaudeCodeAgentAdapter()],
                context.Catalog,
                new FileProjectDefinitionStore(context.Catalog));

            await installer.ReprojectCardTypesAsync(
                context.Project.Id,
                $"http://127.0.0.1:18471/mcp/projects/{context.Project.Id}",
                "test-token",
                CancellationToken.None);

            // Re-projection repairs what is connected; it never introduces an agent the project never
            // installed, because that would be an unasked-for change to someone's working copy.
            Assert.False(Directory.Exists(Path.Combine(context.Project.RootPath, ".claude")));
            Assert.False(Directory.Exists(Path.Combine(context.Project.RootPath, ".zcode")));
        });
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
                var installer = new UnifiedAgentInstaller(adapters, context.Catalog, new FileProjectDefinitionStore(context.Catalog));

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
    public async Task Cline_configuration_uses_its_own_paths_and_the_streamable_http_transport()
    {
        var home = Path.Combine(Path.GetTempPath(), "Aiko.Specs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);
        var originalHome = Environment.GetEnvironmentVariable("AIKO_USER_HOME");
        try
        {
            Environment.SetEnvironmentVariable("AIKO_USER_HOME", home);
            await WithInitializedProjectAsync(async context =>
            {
                var installer = new UnifiedAgentInstaller(
                    [new ClineAgentAdapter()],
                    context.Catalog,
                    new FileProjectDefinitionStore(context.Catalog));
                var applied = await installer.ApplyAsync(
                    context.Project.Id,
                    $"http://127.0.0.1:18471/mcp/projects/{context.Project.Id}",
                    "test-token",
                    ["cline"],
                    CancellationToken.None);
                Assert.All(applied.AdapterResults, result => Assert.True(result.Succeeded));

                // The entry is named after the project's own handle, so several projects can be connected at
                // once and the name a person reads in the file says which project it belongs to.
                var key = $"aiko-{context.Project.Handle}";

                // Both of Cline's global files carry the entry: the app and IDE read the settings file, the
                // CLI reads mcp.json.
                foreach (var path in new[]
                         {
                             Path.Combine(home, ".cline", "mcp.json"),
                             Path.Combine(home, ".cline", "data", "settings", "cline_mcp_settings.json")
                         })
                {
                    var text = await File.ReadAllTextAsync(path);
                    Assert.Contains($"\"{key}\"", text, StringComparison.Ordinal);
                    // The fixture's folder is called Project while the project's handle is project, so this
                    // is what says the key follows the project rather than the folder.
                    Assert.DoesNotContain(
                        $"\"aiko-{Path.GetFileName(context.Project.RootPath)}\"",
                        text,
                        StringComparison.Ordinal);
                    // Cline falls back to the legacy SSE transport when the type is missing, so the
                    // streamable HTTP transport has to be spelled out.
                    Assert.Contains("\"type\": \"streamableHttp\"", text, StringComparison.Ordinal);
                    Assert.Contains("Bearer test-token", text, StringComparison.Ordinal);
                }

                // The workspace carries what Cline reads per project: the rule and the skill per procedure.
                var skill = Path.Combine(
                    context.Project.RootPath, ".cline", "skills", "aiko-run", "SKILL.md");
                Assert.True(File.Exists(skill));
                // A skill is a procedure now, and it announces itself by the name its directory carries.
                var skillText = await File.ReadAllTextAsync(skill);
                Assert.Contains("name: aiko-run", skillText, StringComparison.Ordinal);
                Assert.Contains("description: Run an Aiko card", skillText, StringComparison.Ordinal);
                // No "which project is this" step here: a workspace copy sits in the project and the client
                // that reads it is configured for that project, so only the user-scope copy asks.
                Assert.DoesNotContain("aiko project find", skillText, StringComparison.Ordinal);
                // The per-type procedures reach Cline as skills too, because it has no slash commands.
                Assert.True(File.Exists(Path.Combine(
                    context.Project.RootPath, ".cline", "skills", "aiko-create-task", "SKILL.md")));
                // No project skill is called "aiko": that name belongs to the global skill, which Cline
                // resolves first - the whole reason the workspace one used to be renamed.
                Assert.False(File.Exists(Path.Combine(
                    context.Project.RootPath, ".cline", "skills", "aiko", "SKILL.md")));

                // The working contract is not a skill: Cline reads it from the workspace rule on every run.
                var rule = Path.Combine(context.Project.RootPath, ".clinerules", "aiko.md");
                Assert.True(File.Exists(rule));
                Assert.Contains(
                    "starts with a card",
                    await File.ReadAllTextAsync(rule),
                    StringComparison.Ordinal);

                // Uninstall takes the entry and Aiko's files out again.
                var removed = await installer.UninstallAsync(
                    context.Project.Id, ["cline"], CancellationToken.None);
                Assert.All(removed.AdapterResults, result => Assert.True(result.Succeeded));
                Assert.DoesNotContain(
                    key,
                    await File.ReadAllTextAsync(Path.Combine(home, ".cline", "mcp.json")),
                    StringComparison.Ordinal);
                Assert.False(File.Exists(Path.Combine(context.Project.RootPath, ".clinerules", "aiko.md")));
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
                context.Catalog,
                new FileProjectDefinitionStore(context.Catalog));

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
                // authenticates, so the daemon answers 401 and the agent never sees Aiko. The endpoint
                // carries the project's readable handle - the same one the installer and the diagnosis use.
                var mcp = Path.Combine(context.Project.RootPath, ".mcp.json");
                await File.WriteAllTextAsync(
                    mcp,
                    $"{{\"mcpServers\":{{\"aiko\":{{\"url\":\"http://127.0.0.1:{settings.Port}/mcp/projects/{context.Project.Handle}\"}}}}}}");

                var doctor = new WorkshopDoctor(
                    dataPaths,
                    context.Catalog,
                    new UnifiedAgentInstaller([new ClaudeCodeAgentAdapter()], context.Catalog, new FileProjectDefinitionStore(context.Catalog)),
                    [new ClaudeCodeAgentAdapter()],
                    configuration,
                    new AccessTokenStore(dataPaths),
                    context.Cards,
                    new FileProjectDefinitionStore(context.Catalog),
                    context.Executions);

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
            var installer = new UnifiedAgentInstaller(adapters, context.Catalog, new FileProjectDefinitionStore(context.Catalog));
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
            var installer = new UnifiedAgentInstaller(adapters, context.Catalog, new FileProjectDefinitionStore(context.Catalog));
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
            // ZCode has no rules file, so the contract reaches it through the cross-client AGENTS.md - the
            // very block Codex writes, which is why two adapters merge into one block instead of fighting.
            Assert.Contains("aiko:begin", installedAgents, StringComparison.Ordinal);

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
            var installer = new UnifiedAgentInstaller(adapters, context.Catalog, new FileProjectDefinitionStore(context.Catalog));
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

    [Fact]
    public async Task Renaming_a_card_type_moves_its_board_section_with_it()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var definitions = new FileProjectDefinitionStore(context.Catalog);

            // The type and its board section come from the project's template: the default one now ships an
            // epic pipeline and the epics projection beside stories and tasks, so the spec starts from what a
            // person gets rather than building the same thing by hand.
            var projections = Path.Combine(context.StitchRoot, "projections");
            var projectionPath = Path.Combine(projections, "epics.json");
            Assert.True(
                File.Exists(projectionPath),
                "The default template should ship the epics projection.");

            var renamed = await definitions.RenameWorkflowAsync(
                context.Project.Id,
                "epic",
                "Theme",
                CancellationToken.None);

            // The id is the type's identity: the file carries the new one and the old one is gone.
            Assert.Equal("theme", renamed.Id);
            Assert.Equal(2, renamed.Revision);
            Assert.True(File.Exists(Path.Combine(context.StitchRoot, "workflows", "theme.json")));
            Assert.False(File.Exists(Path.Combine(context.StitchRoot, "workflows", "epic.json")));

            // And the board section follows it, or the type's cards would lose the section that showed them.
            using var projection = JsonDocument.Parse(await File.ReadAllTextAsync(projectionPath));
            Assert.Equal("theme", projection.RootElement.GetProperty("cardKind").GetString());

            // An id another type already holds is refused rather than merged into it.
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                definitions.RenameWorkflowAsync(
                    context.Project.Id, "theme", "task", CancellationToken.None).AsTask());
        });
    }

    /// <summary>
    /// The command queue: what a screen places for an agent survives in the project's own file, and moves
    /// through the states the card page reads.
    /// </summary>
    [Fact]
    public async Task A_placed_command_waits_in_the_projects_own_file_and_can_be_taken_and_closed()
    {
        await WithInitializedProjectAsync(async context =>
        {
            await context.Cards.SaveAsync(
                CreateCard(context.Project.Id, "TASK-001", 1),
                0,
                CancellationToken.None);

            var placed = await context.Commands.PlaceAsync(
                context.Project.Id,
                new PlaceCommandRequest(
                    "TASK-001",
                    CardCommandAction.Start,
                    StageId: "implementation",
                    AgentAdapterId: "cline"),
                CancellationToken.None);

            Assert.Equal("CMD-1", placed.Id);
            Assert.Equal(CardCommandState.Queued, placed.State);
            Assert.Null(placed.ClaimedAtUtc);

            // The queue is a file beside the other documents, which is the whole point: the agent that will
            // carry the command out may not exist when it is placed.
            var queuePath = Path.Combine(context.StitchRoot, "commands.json");
            Assert.True(File.Exists(queuePath), "The command queue should be a document in .aiko.");
            Assert.Contains("CMD-1", await File.ReadAllTextAsync(queuePath), StringComparison.Ordinal);

            var claimed = await context.Commands.ClaimAsync(
                context.Project.Id, placed.Id, "cline", CancellationToken.None);
            Assert.Equal(CardCommandState.Taken, claimed.State);
            Assert.NotNull(claimed.ClaimedAtUtc);

            var finished = await context.Commands.FinishAsync(
                context.Project.Id,
                placed.Id,
                CardCommandState.Completed,
                "Stage started.",
                CancellationToken.None);
            Assert.Equal(CardCommandState.Completed, finished.State);
            Assert.Equal("Stage started.", finished.Message);
            Assert.NotNull(finished.FinishedAtUtc);

            // A closed command is history: the open queue no longer has it, the full list still does.
            Assert.Empty(await context.Commands.ListAsync(context.Project.Id, false, CancellationToken.None));
            var all = await context.Commands.ListAsync(context.Project.Id, true, CancellationToken.None);
            Assert.Single(all);
            Assert.Equal("CMD-1", all[0].Id);

            // Identifiers keep counting, so a removed command can never be confused with the next one.
            var second = await context.Commands.PlaceAsync(
                context.Project.Id,
                new PlaceCommandRequest("TASK-001", CardCommandAction.Start, StageId: "review"),
                CancellationToken.None);
            Assert.Equal("CMD-2", second.Id);
        });
    }

    /// <summary>
    /// One command belongs to one agent: two of them taking it would be the same stage worked twice, and a
    /// command the person placed for a particular agent is not another agent's to take.
    /// </summary>
    [Fact]
    public async Task A_taken_command_cannot_be_taken_again_or_stolen_from_named_agent()
    {
        await WithInitializedProjectAsync(async context =>
        {
            await context.Cards.SaveAsync(
                CreateCard(context.Project.Id, "TASK-001", 1),
                0,
                CancellationToken.None);

            var mine = await context.Commands.PlaceAsync(
                context.Project.Id,
                new PlaceCommandRequest("TASK-001", CardCommandAction.Start, StageId: "backlog"),
                CancellationToken.None);
            var yours = await context.Commands.PlaceAsync(
                context.Project.Id,
                new PlaceCommandRequest(
                    "TASK-001",
                    CardCommandAction.Start,
                    StageId: "backlog",
                    AgentAdapterId: "codex"),
                CancellationToken.None);

            await context.Commands.ClaimAsync(context.Project.Id, mine.Id, "cline", CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                context.Commands.ClaimAsync(
                    context.Project.Id, mine.Id, "codex", CancellationToken.None).AsTask());

            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                context.Commands.ClaimAsync(
                    context.Project.Id, yours.Id, "cline", CancellationToken.None).AsTask());
            Assert.Contains("codex", refusal.Message, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// A command that names nothing an agent could act on is refused at the door rather than queued as a trap
    /// for whoever takes it.
    /// </summary>
    [Fact]
    public async Task An_action_missing_what_it_needs_is_not_queued()
    {
        await WithInitializedProjectAsync(async context =>
        {
            await context.Cards.SaveAsync(
                CreateCard(context.Project.Id, "TASK-001", 1),
                0,
                CancellationToken.None);

            await Assert.ThrowsAsync<ArgumentException>(() =>
                context.Commands.PlaceAsync(
                    context.Project.Id,
                    new PlaceCommandRequest("TASK-001", CardCommandAction.Start),
                    CancellationToken.None).AsTask());
            await Assert.ThrowsAsync<ArgumentException>(() =>
                context.Commands.PlaceAsync(
                    context.Project.Id,
                    new PlaceCommandRequest(
                        "TASK-001",
                        CardCommandAction.Pause,
                        ExecutionId: "execution-1"),
                    CancellationToken.None).AsTask());
            await Assert.ThrowsAsync<ArgumentException>(() =>
                context.Commands.PlaceAsync(
                    context.Project.Id,
                    new PlaceCommandRequest("TASK-404", CardCommandAction.Start, StageId: "backlog"),
                    CancellationToken.None).AsTask());

            // Nothing was written: a refused request must not leave a queued command behind.
            Assert.Empty(await context.Commands.ListAsync(context.Project.Id, true, CancellationToken.None));
            Assert.False(File.Exists(Path.Combine(context.StitchRoot, "commands.json")));
        });
    }

    /// <summary>
    /// A command an agent holds is closed by that agent: a screen cannot know whether the work it asked for
    /// has happened, so withdrawing it would be a guess.
    /// </summary>
    [Fact]
    public async Task Only_a_command_nobody_took_can_be_withdrawn()
    {
        await WithInitializedProjectAsync(async context =>
        {
            await context.Cards.SaveAsync(
                CreateCard(context.Project.Id, "TASK-001", 1),
                0,
                CancellationToken.None);

            var first = await context.Commands.PlaceAsync(
                context.Project.Id,
                new PlaceCommandRequest("TASK-001", CardCommandAction.Start, StageId: "backlog"),
                CancellationToken.None);
            var withdrawn = await context.Commands.CancelAsync(
                context.Project.Id, first.Id, "Not needed after all.", CancellationToken.None);
            Assert.Equal(CardCommandState.Cancelled, withdrawn.State);
            Assert.Equal("Not needed after all.", withdrawn.Message);

            var second = await context.Commands.PlaceAsync(
                context.Project.Id,
                new PlaceCommandRequest("TASK-001", CardCommandAction.Start, StageId: "backlog"),
                CancellationToken.None);
            await context.Commands.ClaimAsync(context.Project.Id, second.Id, "cline", CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                context.Commands.CancelAsync(context.Project.Id, second.Id, null, CancellationToken.None)
                    .AsTask());
        });
    }

    /// <summary>
    /// Working the board is one command that names no card, and a project works its board once.
    /// </summary>
    [Fact]
    public async Task Working_the_board_is_one_command_that_names_no_card()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var pass = await context.Commands.PlaceAsync(
                context.Project.Id,
                new PlaceCommandRequest(null, CardCommandAction.RunBoard),
                CancellationToken.None);

            Assert.Equal("CMD-1", pass.Id);
            Assert.Null(pass.CardId);
            Assert.Equal(CardCommandState.Queued, pass.State);

            // A pass is not about one card and not about one run: naming either is refused rather than
            // quietly ignored, because a field nobody reads is how a request and its meaning drift apart.
            await Assert.ThrowsAsync<ArgumentException>(() =>
                context.Commands.PlaceAsync(
                    context.Project.Id,
                    new PlaceCommandRequest("TASK-001", CardCommandAction.RunBoard),
                    CancellationToken.None).AsTask());
            await Assert.ThrowsAsync<ArgumentException>(() =>
                context.Commands.PlaceAsync(
                    context.Project.Id,
                    new PlaceCommandRequest(null, CardCommandAction.Start, StageId: "backlog"),
                    CancellationToken.None).AsTask());

            // A card action without its card has nothing to act on.
            await Assert.ThrowsAsync<ArgumentException>(() =>
                context.Commands.PlaceAsync(
                    context.Project.Id,
                    new PlaceCommandRequest(null, CardCommandAction.Resume, ExecutionId: "run-1"),
                    CancellationToken.None).AsTask());

            // One project works its board once: while the pass is open a second one is refused. Two passes
            // would take the cards in the same order and interleave, and neither stop would mean anything.
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                context.Commands.PlaceAsync(
                    context.Project.Id,
                    new PlaceCommandRequest(null, CardCommandAction.RunBoard),
                    CancellationToken.None).AsTask());
            Assert.Contains("already being worked", refusal.Message, StringComparison.Ordinal);

            // ...and once it is closed, the button can be pressed again.
            await context.Commands.ClaimAsync(context.Project.Id, pass.Id, "cline", CancellationToken.None);
            await context.Commands.FinishAsync(
                context.Project.Id,
                pass.Id,
                CardCommandState.Completed,
                "Two cards done.",
                CancellationToken.None);
            var again = await context.Commands.PlaceAsync(
                context.Project.Id,
                new PlaceCommandRequest(null, CardCommandAction.RunBoard),
                CancellationToken.None);
            Assert.Equal("CMD-2", again.Id);
        });
    }

    /// <summary>
    /// The pass over the board reaches an agent as a procedure, and the procedure is where its rules live -
    /// a pass is text, not an engine, so what it says is the whole implementation.
    /// </summary>
    [Fact]
    public async Task The_board_pass_reaches_an_agent_and_says_where_it_stops()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var installer = new UnifiedAgentInstaller(
                [new ClineAgentAdapter()],
                context.Catalog,
                new FileProjectDefinitionStore(context.Catalog));
            await installer.ApplyAsync(
                context.Project.Id,
                $"http://127.0.0.1:18471/mcp/projects/{context.Project.Id}",
                "test-token",
                ["cline"],
                CancellationToken.None);

            var procedure = await File.ReadAllTextAsync(Path.Combine(
                context.Project.RootPath, ".cline", "skills", "aiko-run-all", "SKILL.md"));

            // The order is the board's own computed priority, not the cards sorted by hand.
            Assert.Contains("aiko_list_board", procedure, StringComparison.Ordinal);
            // A blocked card is a skip read from the refusal - the rule keeps its one home.
            Assert.Contains("refuses it", procedure, StringComparison.Ordinal);
            // A question pauses the card and the pass goes on ...
            Assert.Contains("do not stop the pass", procedure, StringComparison.Ordinal);
            Assert.Contains("waiting-for-user", procedure, StringComparison.Ordinal);
            // ... while a failure, a limit or a forbidden action stops it.
            Assert.Contains("rate limit", procedure, StringComparison.Ordinal);
            // Resumability is claimed to need no bookkeeping, and that claim is checked by a human reading
            // it - what a spec can pin is that the pass says so at all.
            Assert.Contains("no bookkeeping", procedure, StringComparison.Ordinal);
            // The command that asked for the pass is closed either way.
            Assert.Contains("aiko_finish_command", procedure, StringComparison.Ordinal);
            // And it names the adapter it was installed for, so the stages it starts record the right agent.
            Assert.Contains("\"cline\"", procedure, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// The procedures an agent runs carry the same rule as the contract: the state of the project is read
    /// through the tools, and a stage's artifact is written through them too.
    /// </summary>
    [Fact]
    public async Task The_run_procedures_forbid_reading_the_project_state_from_files()
    {
        await WithInitializedProjectAsync(async context =>
        {
            var installer = new UnifiedAgentInstaller(
                [new ClineAgentAdapter()],
                context.Catalog,
                new FileProjectDefinitionStore(context.Catalog));
            await installer.ApplyAsync(
                context.Project.Id,
                $"http://127.0.0.1:18471/mcp/projects/{context.Project.Id}",
                "test-token",
                ["cline"],
                CancellationToken.None);

            foreach (var procedure in new[] { "aiko-run", "aiko-run-all" })
            {
                var text = await File.ReadAllTextAsync(Path.Combine(
                    context.Project.RootPath, ".cline", "skills", procedure, "SKILL.md"));
                Assert.Contains("Do not open a file under .aiko", text, StringComparison.Ordinal);
                Assert.Contains("aiko_save_card_artifact", text, StringComparison.Ordinal);
                Assert.Contains("is the exception", text, StringComparison.Ordinal);
            }
        });
    }

    /// <summary>
    /// A card of a freshly invented type, with the pipeline the specs below do not care about.
    /// </summary>
    internal static Card CreateCard(string projectId, string cardId, long revision) =>
        new(
            new CardReference(projectId, cardId),
            "Task",
            cardId,
            "task",
            "backlog",
            revision,
            1m,
            [],
            [],
            new Dictionary<string, string>());

    /// <summary>
    /// Records an estimate on the card, which is what a stage's completion now requires: the moment of the
    /// estimate goes into the card's metadata through the same key the estimate tools write.
    /// </summary>
    private static async Task EstimateAsync(TestContext context, Card card)
    {
        var current = await context.Cards.FindAsync(card.Reference, CancellationToken.None)
            ?? throw new InvalidOperationException($"Unknown card: {card.Reference.CardId}");
        var metadata = new Dictionary<string, string>(current.Metadata, StringComparer.Ordinal)
        {
            [Card.EstimatedAtMetadataKey] =
                DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
        };
        await context.Cards.SaveAsync(
            current with { Metadata = metadata, Revision = current.Revision + 1 },
            current.Revision,
            CancellationToken.None);
    }

    /// <summary>
    /// An initialized project and the stores wired to an isolated database. Shared with the layout specs,
    /// which need the same project to file cards in.
    /// </summary>
    internal static async Task WithInitializedProjectAsync(Func<TestContext, Task> assertion)
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
            var appSettingsStore = new FileAppSettingsStore(catalog);
            var initializer = new ProjectInitializer(
                catalog,
                reindexer,
                appSettingsStore,
                new FileProjectTemplateStore(
                    new AikoDataPaths(Path.Combine(testRoot, "data", "aiko.db"))));
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
                new SqliteAikoEventStore(database, catalog),
                new FileCardCommandStore(catalog, cards)));
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
