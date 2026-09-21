using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Aiko.Application.Contracts;
using Aiko.Application.Installation;
using Aiko.Infrastructure.Installation;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// The installation engine: one path for install and update, <c>--check</c> that changes nothing, and a
/// daemon that is stopped before the files are replaced and brought back after.
/// </summary>
/// <remarks>
/// Everything runs on a local feed, a temporary install directory and a fake daemon, so the properties the
/// story asks for are shown without a network, without a process and without going near the owner's
/// installation or their <c>PATH</c>.
/// </remarks>
public sealed class InstallationEngineSpecs
{
    [Fact]
    public async Task An_install_writes_the_release_and_records_what_it_installed()
    {
        using var fixture = new EngineFixture();
        fixture.Publish("v0.9.2");

        var report = await Fixture(fixture, new FakeDaemon()).InstallAsync(
            fixture.Request("v0.9.2"),
            CancellationToken.None);

        Assert.Equal(InstallationOutcome.Installed, report.Outcome);
        Assert.Null(report.Installed);
        Assert.Equal("v0.9.2", report.Available?.Tag);
        Assert.Equal("the release", File.ReadAllText(Path.Combine(fixture.InstallDirectory, "aiko.exe")));

        // The record is what a later run reads to decide that it is already up to date.
        var recorded = InstalledVersionFile.Read(fixture.InstallDirectory);
        Assert.Equal("v0.9.2", recorded?.Tag);
        Assert.Equal("0.9.2", recorded?.Version);

        // Staging is beside the installation and gone once it is in place.
        Assert.Empty(Directory.GetDirectories(fixture.Root, "*.staging-*"));
        Assert.Empty(Directory.GetFiles(fixture.Root, "*.staging-*.zip"));
    }

    [Fact]
    public async Task A_report_only_run_changes_nothing_and_says_what_is_available()
    {
        using var fixture = new EngineFixture();
        fixture.Publish("v0.9.2");
        fixture.Publish("v0.9.3");
        var daemon = new FakeDaemon { Running = true };

        var engine = Fixture(fixture, daemon);
        await engine.InstallAsync(fixture.Request("v0.9.2"), CancellationToken.None);
        var before = fixture.Fingerprint();
        daemon.Calls.Clear();
        fixture.Repair.Selections.Clear();

        var report = await engine.InstallAsync(
            fixture.Request("v0.9.3") with { Check = true },
            CancellationToken.None);

        Assert.Equal(InstallationOutcome.Reported, report.Outcome);
        Assert.Equal("v0.9.2", report.Installed?.Tag);
        Assert.Equal("v0.9.3", report.Available?.Tag);
        Assert.Contains("v0.9.2", report.Summary, StringComparison.Ordinal);
        Assert.Contains("v0.9.3", report.Summary, StringComparison.Ordinal);

        // The fingerprint is the claim, not the text: a check that reported without changing anything is
        // only as good as the directory it left behind.
        Assert.Equal(before, fixture.Fingerprint());
        Assert.Empty(Directory.GetDirectories(fixture.Root, "*.staging-*"));

        // Nothing was asked of the daemon and nothing of the repair: a report is a report.
        Assert.Empty(daemon.Calls);
        Assert.Empty(fixture.Repair.Selections);
    }

    [Fact]
    public async Task An_update_stops_the_daemon_before_the_files_and_starts_it_afterwards()
    {
        using var fixture = new EngineFixture();
        fixture.Publish("v0.9.2");
        fixture.Publish("v0.9.3");
        var daemon = new FakeDaemon { Running = true };

        var engine = Fixture(fixture, daemon);
        await engine.InstallAsync(fixture.Request("v0.9.2"), CancellationToken.None);
        daemon.Calls.Clear();

        var report = await engine.UpdateAsync(fixture.Request("v0.9.3"), CancellationToken.None);

        Assert.Equal(InstallationOutcome.Updated, report.Outcome);
        Assert.Equal(["stop", "start"], daemon.Calls);
        Assert.Equal("v0.9.3", InstalledVersionFile.Read(fixture.InstallDirectory)?.Tag);
        Assert.Equal("the release", File.ReadAllText(Path.Combine(fixture.InstallDirectory, "aiko.exe")));
    }
    [Fact]
    public async Task A_daemon_that_will_not_stop_is_refused_and_the_installation_is_left_alone()
    {
        using var fixture = new EngineFixture();
        fixture.Publish("v0.9.2");
        fixture.Publish("v0.9.3");
        var daemon = new FakeDaemon { Running = true, StopRefusal = "The daemon refused the access token." };

        var engine = Fixture(fixture, daemon);
        await engine.InstallAsync(fixture.Request("v0.9.2"), CancellationToken.None);
        var before = fixture.Fingerprint();

        var report = await engine.UpdateAsync(fixture.Request("v0.9.3"), CancellationToken.None);

        Assert.Equal(InstallationOutcome.Refused, report.Outcome);
        Assert.Equal("The daemon refused the access token.", report.Summary);
        Assert.Equal(before, fixture.Fingerprint());

        // Refusing before the replacement means the staged release is discarded with it: half a release
        // beside the installation is what the next run would have to guess about.
        Assert.Empty(Directory.GetDirectories(fixture.Root, "*.staging-*"));
        Assert.Empty(fixture.Repair.Selections);
    }

    [Fact]
    public async Task An_older_release_is_refused_without_force_and_replaces_with_it()
    {
        using var fixture = new EngineFixture();
        fixture.Publish("v0.9.2");
        fixture.Publish("v0.8.0");

        var engine = Fixture(fixture, new FakeDaemon());
        await engine.InstallAsync(fixture.Request("v0.9.2"), CancellationToken.None);
        var before = fixture.Fingerprint();

        var refused = await engine.UpdateAsync(fixture.Request("v0.8.0"), CancellationToken.None);

        Assert.Equal(InstallationOutcome.Refused, refused.Outcome);
        Assert.Contains("--force", refused.Summary, StringComparison.Ordinal);
        Assert.Equal(before, fixture.Fingerprint());

        var forced = await engine.UpdateAsync(
            fixture.Request("v0.8.0") with { Force = true },
            CancellationToken.None);

        Assert.Equal(InstallationOutcome.Updated, forced.Outcome);
        Assert.Equal("v0.8.0", InstalledVersionFile.Read(fixture.InstallDirectory)?.Tag);
    }

    [Fact]
    public async Task The_same_version_is_a_no_op_that_does_not_even_look_at_the_release()
    {
        using var fixture = new EngineFixture();
        fixture.Publish("v0.9.2");
        var counting = new CountingSource(fixture.Source);

        var engine = new InstallationEngine(
            counting,
            new FakeDaemon(),
            fixture.Repair,
            fixture.Diagnostics);
        await engine.InstallAsync(fixture.Request("v0.9.2"), CancellationToken.None);
        var before = fixture.Fingerprint();
        var downloadsBefore = counting.Downloads;
        fixture.Repair.Selections.Clear();

        var report = await engine.UpdateAsync(fixture.Request("v0.9.2"), CancellationToken.None);

        Assert.Equal(InstallationOutcome.UpToDate, report.Outcome);
        Assert.Equal(before, fixture.Fingerprint());
        Assert.Equal(downloadsBefore, counting.Downloads);
        Assert.Empty(fixture.Repair.Selections);
    }

    [Fact]
    public async Task The_repair_runs_with_the_agents_the_run_named_and_the_doctor_answers_last()
    {
        using var fixture = new EngineFixture();
        fixture.Publish("v0.9.3");

        var report = await Fixture(fixture, new FakeDaemon()).InstallAsync(
            fixture.Request("v0.9.3") with { Agents = ["claude-code", "codex"] },
            CancellationToken.None);

        var selection = Assert.Single(fixture.Repair.Selections);
        Assert.Equal(["claude-code", "codex"], selection.Agents);
        Assert.False(selection.NoAgents);

        // The repair's own steps are in the report, and the doctor closes it: an update that left a stale
        // agent configuration behind has not finished its job.
        Assert.Contains(report.Steps, step => step.Name == "reindex");
        var doctor = Assert.Single(report.Steps, step => step.Name == "doctor");
        Assert.True(doctor.Succeeded);
        Assert.Contains("need attention", doctor.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void No_agents_means_nobody_rather_than_whatever_is_configured()
    {
        Assert.Equal(
            ["claude-code"],
            InstallationAgentSelection.Configured.Targets(["claude-code"]));
        Assert.Empty(new InstallationAgentSelection(null, NoAgents: true).Targets(["claude-code"]));
        Assert.Equal(
            ["codex"],
            new InstallationAgentSelection(["codex"], NoAgents: false).Targets(["claude-code"]));
    }
    /// <summary>The engine under test, wired to a local feed and to what the run must not do twice.</summary>
    private static InstallationEngine Fixture(EngineFixture fixture, FakeDaemon daemon) =>
        new(fixture.Source, daemon, fixture.Repair, fixture.Diagnostics);

    /// <summary>A temporary installation, its feed and the fakes the engine asks.</summary>
    private sealed class EngineFixture : IDisposable
    {
        public EngineFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"aiko-engine-{Guid.NewGuid():N}");
            Feed = Path.Combine(Root, "feed");
            InstallDirectory = Path.Combine(Root, "bin");
            Directory.CreateDirectory(Feed);

            // What is already there before the first run the specs make: a directory that is on nobody's
            // PATH, holding an older build and no install.json, which is what "installed before the record
            // existed" looks like.
            Directory.CreateDirectory(InstallDirectory);
            File.WriteAllText(Path.Combine(InstallDirectory, ReleaseLayout.CliFileName), "the installed version");
        }

        public string Root { get; }

        public string Feed { get; }

        public string InstallDirectory { get; }

        public FakeRepair Repair { get; } = new();

        public FakeDiagnostics Diagnostics { get; } = new();

        public IReleaseSource Source => new LocalReleaseSource(Feed);

        public InstallationRequest Request(string tag) =>
            new(InstallDirectory, Tag: tag, UpdatePath: false);

        /// <summary>Publishes a release: an archive with the required layout, and its published checksum.</summary>
        public void Publish(string tag)
        {
            var releaseDirectory = Path.Combine(Feed, tag);
            Directory.CreateDirectory(releaseDirectory);
            var assetName = ReleaseNaming.AssetNameForTag(tag);
            var archivePath = Path.Combine(releaseDirectory, assetName);

            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                foreach (var entry in ReleaseLayout.RequiredEntries)
                {
                    using var writer = new StreamWriter(archive.CreateEntry(entry).Open());
                    writer.Write("the release");
                }
            }

            File.WriteAllText(
                Path.Combine(releaseDirectory, ReleaseNaming.ChecksumsAssetName),
                $"{Sha256Of(archivePath)}  {assetName}");
        }

        /// <summary>
        /// The installation's own fingerprint: every file by name and content. Comparing two of these is how
        /// "nothing was written" is checked without trusting the report's own words.
        /// </summary>
        public string Fingerprint()
        {
            var builder = new StringBuilder();
            foreach (var file in Directory
                .EnumerateFiles(InstallDirectory, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal))
            {
                builder
                    .Append(Path.GetRelativePath(InstallDirectory, file))
                    .Append('|')
                    .Append(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))))
                    .Append('\n');
            }

            return builder.ToString();
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }

        private static string Sha256Of(string path) =>
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    }
    /// <summary>
    /// A daemon that answers only when the spec says so, and remembers what was asked of it in order - which
    /// is how "stopped before the files, started after" is asserted rather than assumed.
    /// </summary>
    private sealed class FakeDaemon : IDaemonLifecycle
    {
        public bool Running { get; set; }

        /// <summary>When set, a stop refuses with this message instead of stopping.</summary>
        public string? StopRefusal { get; init; }

        public List<string> Calls { get; } = [];

        public ValueTask<bool> IsRunningAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(Running);

        public ValueTask StopAsync(CancellationToken cancellationToken)
        {
            Calls.Add("stop");
            if (StopRefusal is not null)
            {
                throw new InstallationRefusedException(StopRefusal);
            }

            Running = false;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> StartAsync(string installDirectory, CancellationToken cancellationToken)
        {
            Calls.Add("start");
            Running = true;
            return ValueTask.FromResult(true);
        }
    }

    /// <summary>A repair that records what it was asked to do and does nothing to the machine.</summary>
    private sealed class FakeRepair : IInstallationRepair
    {
        public List<InstallationAgentSelection> Selections { get; } = [];

        public ValueTask<IReadOnlyList<InstallationStep>> RepairAsync(
            InstallationAgentSelection selection,
            CancellationToken cancellationToken)
        {
            Selections.Add(selection);
            return ValueTask.FromResult<IReadOnlyList<InstallationStep>>(
                [new InstallationStep("reindex", true, "Sample: 1 cards, 0 relations, 0 memory documents.")]);
        }
    }

    /// <summary>A doctor that always has one finding, so the step's detail can be asserted.</summary>
    private sealed class FakeDiagnostics : IWorkshopDiagnostics
    {
        public ValueTask<WorkshopDiagnostics> InspectAsync(
            string? projectId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new WorkshopDiagnostics(
            [
                new DiagnosticFinding(
                    DiagnosticFinding.AgentConfigArea,
                    DiagnosticSeverity.Warning,
                    "An agent configuration still names the old endpoint.")
            ]));
    }

    /// <summary>A source that counts downloads, so "the release was never fetched" can be asserted.</summary>
    private sealed class CountingSource(IReleaseSource inner) : IReleaseSource
    {
        public int Downloads { get; private set; }

        public ValueTask<string> ResolveTagAsync(string? requestedTag, CancellationToken cancellationToken) =>
            inner.ResolveTagAsync(requestedTag, cancellationToken);

        public ValueTask DownloadAssetAsync(
            string tag,
            string assetName,
            string destinationPath,
            CancellationToken cancellationToken)
        {
            Downloads++;
            return inner.DownloadAssetAsync(tag, assetName, destinationPath, cancellationToken);
        }

        public ValueTask<IReadOnlyDictionary<string, string>> ReadChecksumsAsync(
            string tag,
            CancellationToken cancellationToken) =>
            inner.ReadChecksumsAsync(tag, cancellationToken);
    }
}
