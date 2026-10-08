using System.Security.Cryptography;
using Aiko.Application.Contracts;
using Aiko.Infrastructure.Diagnostics;
using Aiko.Infrastructure.Projects;
using Aiko.Infrastructure.Storage;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// The integrity check of a project's own data: what "intact" means, and what a person who edits the files
/// themselves should see. The check is the half that reads; the seal is the half that writes, and it exists
/// because a baseline that refreshed itself on every save would fold a manual edit into itself and then call
/// the result intact.
/// </summary>
public sealed class ProjectIntegritySpecs
{
    [Fact]
    public async Task A_project_with_no_baseline_says_so_instead_of_calling_itself_intact()
    {
        await using var project = await ProjectHarness.CreateAsync();

        var report = await project.Integrity.VerifyAsync(project.Project.Id, CancellationToken.None);

        Assert.False(report.HasBaseline);
        Assert.True(report.HasProblems);
        var finding = Assert.Single(report.Findings);
        Assert.Contains("--seal", finding.Summary, StringComparison.Ordinal);
        // Reading is reading: a check on a project nobody sealed writes no baseline of its own.
        Assert.False(File.Exists(project.ManifestPath));
    }

    [Fact]
    public async Task A_seal_records_the_data_and_the_check_after_it_is_clean()
    {
        await using var project = await ProjectHarness.CreateAsync();

        var sealedReport = await project.Integrity.SealAsync(project.Project.Id, CancellationToken.None);
        var checkedReport = await project.Integrity.VerifyAsync(project.Project.Id, CancellationToken.None);

        Assert.True(File.Exists(project.ManifestPath));
        // The seal answers with the check that follows it, so a seal that did not take cannot look successful.
        Assert.Empty(sealedReport.Findings);
        Assert.False(sealedReport.HasProblems);
        Assert.Empty(checkedReport.Findings);
        Assert.False(checkedReport.HasProblems);
        Assert.True(checkedReport.HasBaseline);
        Assert.NotNull(checkedReport.SealedAt);
        Assert.True(checkedReport.ComparedFiles > 0);
        Assert.Equal(sealedReport.ComparedFiles, checkedReport.ComparedFiles);
    }

    [Fact]
    public async Task A_document_a_person_edited_by_hand_is_reported_as_changed()
    {
        await using var project = await ProjectHarness.CreateAsync();
        await project.Integrity.SealAsync(project.Project.Id, CancellationToken.None);

        await project.WriteAsync("memory/conventions/naming.md", "# Naming\n\nEdited by hand.\n");

        var report = await project.Integrity.VerifyAsync(project.Project.Id, CancellationToken.None);

        var finding = Assert.Single(report.Findings);
        Assert.Equal(IntegrityFindingKind.Changed, finding.Kind);
        Assert.Equal(DiagnosticSeverity.Warning, finding.Severity);
        Assert.Equal("memory/conventions/naming.md", finding.Path);
        Assert.True(report.HasProblems);
    }

    [Fact]
    public async Task A_file_added_by_hand_is_reported_and_is_not_a_problem()
    {
        await using var project = await ProjectHarness.CreateAsync();
        await project.Integrity.SealAsync(project.Project.Id, CancellationToken.None);

        await project.WriteAsync("memory/decisions/keep-this.md", "# Keep this\n");

        var report = await project.Integrity.VerifyAsync(project.Project.Id, CancellationToken.None);

        // A project whose data lives in files gains them whenever its owner likes. The check says what appeared
        // and leaves the judgement to the reader - calling a new file damage would make the whole check useless
        // to the person it is for.
        var finding = Assert.Single(report.Findings);
        Assert.Equal(IntegrityFindingKind.Added, finding.Kind);
        Assert.Equal(DiagnosticSeverity.Ok, finding.Severity);
        Assert.Equal("memory/decisions/keep-this.md", finding.Path);
        Assert.False(report.HasProblems);
    }

    [Fact]
    public async Task A_file_the_baseline_lists_and_the_disk_lost_is_reported_as_missing()
    {
        await using var project = await ProjectHarness.CreateAsync();
        await project.Integrity.SealAsync(project.Project.Id, CancellationToken.None);

        File.Delete(project.PathOf("memory/conventions/naming.md"));

        var report = await project.Integrity.VerifyAsync(project.Project.Id, CancellationToken.None);

        var finding = Assert.Single(report.Findings);
        Assert.Equal(IntegrityFindingKind.Missing, finding.Kind);
        Assert.Equal(DiagnosticSeverity.Warning, finding.Severity);
        Assert.Equal("memory/conventions/naming.md", finding.Path);
        Assert.True(report.HasProblems);
    }

    [Fact]
    public async Task A_document_nothing_can_parse_is_an_error_naming_the_file()
    {
        await using var project = await ProjectHarness.CreateAsync();
        await project.Integrity.SealAsync(project.Project.Id, CancellationToken.None);

        // Half a card file is what an interrupted write or a bad merge leaves behind, and the readers that
        // rebuild the board throw on it - so it is reported as the error it is rather than as mere drift.
        await project.WriteAsync("workflows/tasks/TASK-1/card.json", "{\n  \"id\": \"TASK-1\",\n");

        var report = await project.Integrity.VerifyAsync(project.Project.Id, CancellationToken.None);

        Assert.Contains(report.Findings, finding =>
            finding.Kind == IntegrityFindingKind.Document &&
            finding.Severity == DiagnosticSeverity.Error &&
            finding.Path == "workflows/tasks/TASK-1/card.json");
        // The same file is also a change against the baseline. Both lines are worth having and they say
        // different things: one that nothing can read it, the other that it did not look like this when the
        // baseline was sealed.
        Assert.Contains(report.Findings, finding => finding.Kind == IntegrityFindingKind.Changed);
    }

    [Fact]
    public async Task The_daemons_own_working_state_is_out_of_the_baseline()
    {
        await using var project = await ProjectHarness.CreateAsync();
        await project.Integrity.SealAsync(project.Project.Id, CancellationToken.None);
        var sealedBaseline = await File.ReadAllTextAsync(project.ManifestPath);

        Assert.DoesNotContain("\"projections/", sealedBaseline, StringComparison.Ordinal);
        Assert.DoesNotContain("\"runtime/", sealedBaseline, StringComparison.Ordinal);

        // A rebuilt projection and a run journal are Aiko's own state and change while the daemon works:
        // recording them would turn ordinary work into a finding about the user's data.
        await project.WriteAsync("projections/board.db", "rebuilt");
        await project.WriteAsync("runtime/runs/01run.json", "{\"state\":\"completed\"}");

        var report = await project.Integrity.VerifyAsync(project.Project.Id, CancellationToken.None);

        Assert.Empty(report.Findings);
        Assert.False(report.HasProblems);
    }

    [Fact]
    public async Task A_check_leaves_every_file_as_it_found_it_and_a_seal_writes_only_the_baseline()
    {
        await using var project = await ProjectHarness.CreateAsync();
        await project.Integrity.SealAsync(project.Project.Id, CancellationToken.None);
        var before = project.Snapshot();

        var report = await project.Integrity.VerifyAsync(project.Project.Id, CancellationToken.None);

        Assert.Empty(report.Findings);
        Assert.Equal(before, project.Snapshot());

        // The seal is the one write the pair has, and it writes one file: the baseline describing the data it
        // was just asked about. Nothing under the project is touched.
        await project.Integrity.SealAsync(project.Project.Id, CancellationToken.None);

        var after = project.Snapshot();
        Assert.Equal(before.Length, after.Length);
        Assert.All(
            before.Where(line => !line.StartsWith("manifest.json=", StringComparison.Ordinal)),
            line => Assert.Contains(line, after));
        // The baseline itself is the one line that differs: a second seal is a new baseline rather than a copy
        // of the old one, so a check can tell that nothing was recorded in between.
        Assert.NotEqual(
            before.Single(line => line.StartsWith("manifest.json=", StringComparison.Ordinal)),
            after.Single(line => line.StartsWith("manifest.json=", StringComparison.Ordinal)));
    }
}

/// <summary>
/// A registered project whose <c>.aiko</c> is written by hand.
/// </summary>
/// <remarks>
/// The check hashes files and parses them as JSON, so what they hold only has to be readable - no card schema,
/// no workflow, no daemon - and a spec can then change one file the way a person with an editor would.
/// </remarks>
internal sealed class ProjectHarness : IAsyncDisposable
{
    private readonly string root;

    private ProjectHarness(string root, SqliteProjectCatalog catalog, RegisteredProject project)
    {
        this.root = root;
        Project = project;
        Integrity = new FileProjectIntegrity(catalog);
    }

    public RegisteredProject Project { get; }

    public FileProjectIntegrity Integrity { get; }

    public string Root => root;

    public string DataRoot => AikoProjectPaths.DataRoot(Project.RootPath);

    /// <summary>Where the baseline of this project lives.</summary>
    public string ManifestPath => AikoProjectPaths.IntegrityManifest(Project.RootPath);

    public static async ValueTask<ProjectHarness> CreateAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aiko-integrity-{Guid.NewGuid():N}");
        var databasePath = Path.Combine(root, "data", "aiko.db");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        var database = new AikoDatabase(new AikoDataPaths(databasePath));
        await database.InitializeAsync();

        var project = new RegisteredProject(
            "01aikointegritytest",
            "Integrity test",
            Path.Combine(root, "project"),
            "integrity-test");
        var catalog = new SqliteProjectCatalog(database);
        await catalog.SaveAsync(project, CancellationToken.None);

        var harness = new ProjectHarness(root, catalog, project);
        await harness.WriteAsync("project.json", "{\n  \"schemaVersion\": 1\n}\n");
        await harness.WriteAsync("settings.json", "{\n  \"schemaVersion\": 1\n}\n");
        await harness.WriteAsync("memory/index.md", "# Index\n");
        await harness.WriteAsync("memory/conventions/naming.md", "# Naming\n");
        await harness.WriteAsync("workflows/task.json", "{\n  \"id\": \"task\"\n}\n");
        await harness.WriteAsync("workflows/tasks/TASK-1/card.json", "{\n  \"id\": \"TASK-1\"\n}\n");
        await harness.WriteAsync("projections/board.db", "sqlite");
        await harness.WriteAsync("runtime/runs/01run.json", "{\n  \"state\": \"running\"\n}\n");
        return harness;
    }

    public string PathOf(string relativePath) =>
        Path.Combine(DataRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

    public async ValueTask WriteAsync(string relativePath, string content)
    {
        var path = PathOf(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
    }

    /// <summary>
    /// Every file under <c>.aiko</c> as a sorted <c>path=sha256</c> line, so a spec can say that nothing changed
    /// rather than only that the files it looked at did not.
    /// </summary>
    public string[] Snapshot() =>
    [
        .. Directory
            .EnumerateFiles(DataRoot, "*", SearchOption.AllDirectories)
            .Select(file =>
                $"{Path.GetRelativePath(DataRoot, file).Replace('\\', '/')}=" +
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))))
            .Order(StringComparer.Ordinal)
    ];

    public ValueTask DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        return ValueTask.CompletedTask;
    }
}
