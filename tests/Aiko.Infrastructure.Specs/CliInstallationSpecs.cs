using System.IO.Compression;
using System.Security.Cryptography;
using Aiko.Application.Installation;
using Aiko.Infrastructure.Installation;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// The installation commands, run the way a person runs them: the built CLI against a data directory, an
/// installation and a feed of its own.
/// </summary>
/// <remarks>
/// What this covers is the shape the story settled on - the bootstrap script fetches and unpacks a release and
/// the freshly unpacked CLI finishes the job - together with the two answers a person acts on: the version an
/// installation reports, and what <c>--check</c> says without changing anything. The feed is a directory, so
/// none of it needs a network, a real release or the owner's own installation.
/// </remarks>
public sealed class CliInstallationSpecs
{
    [Fact]
    public async Task The_version_is_the_recorded_one_and_this_build_when_nothing_is_installed()
    {
        using var fixture = new CliInstallationFixture();

        // Nothing installed in that directory: the answer is this build's own version. An empty or invented
        // answer would be worse than none, so it is neither.
        var (exitCode, output) = await fixture.RunAsync(
            "--version", "--install-dir", fixture.InstallDirectory);
        Assert.Equal(0, exitCode);
        Assert.False(string.IsNullOrWhiteSpace(output), "`aiko --version` printed nothing.");
        Assert.NotEqual("unknown", output.Trim());

        // An installation made by this engine answers with the tag it recorded.
        InstalledVersionFile.Write(
            fixture.InstallDirectory,
            new InstalledVersion(
                fixture.Tag,
                ReleaseNaming.VersionFromTag(fixture.Tag),
                InstalledVersion.LatestChannel,
                DateTimeOffset.UtcNow,
                fixture.InstallDirectory));

        (exitCode, output) = await fixture.RunAsync(
            "--version", "--install-dir", fixture.InstallDirectory);
        Assert.Equal(0, exitCode);
        Assert.Equal(fixture.Tag, output.Trim());
    }

    [Fact]
    public async Task Finishing_an_install_puts_the_staged_release_in_place_and_records_it()
    {
        using var fixture = new CliInstallationFixture();
        fixture.Stage();

        var (exitCode, output) = await fixture.RunAsync(
            "install", "--from", fixture.Staged, "--tag", fixture.Tag,
            "--install-dir", fixture.InstallDirectory, "--no-path", "--no-agents");

        Assert.True(exitCode == 0, output);
        Assert.Equal(
            "the release",
            File.ReadAllText(Path.Combine(fixture.InstallDirectory, ReleaseLayout.CliFileName)));

        // The record is what `aiko --version` and the next update read.
        var recorded = InstalledVersionFile.Read(fixture.InstallDirectory);
        Assert.Equal(fixture.Tag, recorded?.Tag);
        Assert.Contains("Installed", output, StringComparison.Ordinal);

        // The staging directory is beside the installation and gone once the release is in place.
        Assert.False(Directory.Exists(fixture.Staged));
    }

    [Fact]
    public async Task A_staging_directory_that_is_not_a_release_is_refused_and_nothing_is_installed()
    {
        using var fixture = new CliInstallationFixture();

        // What a download that died half way leaves behind: a directory with some of the files in it.
        Directory.CreateDirectory(fixture.Staged);
        File.WriteAllText(
            Path.Combine(fixture.Staged, ReleaseLayout.CliFileName),
            "half a release");

        var (exitCode, output) = await fixture.RunAsync(
            "install", "--from", fixture.Staged, "--tag", fixture.Tag,
            "--install-dir", fixture.InstallDirectory, "--no-path", "--no-agents");

        Assert.Equal(1, exitCode);
        Assert.Contains("does not carry", output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(fixture.InstallDirectory));
    }

    [Fact]
    public async Task A_report_only_update_says_what_is_installed_and_what_is_available_and_writes_nothing()
    {
        using var fixture = new CliInstallationFixture();
        fixture.Stage();
        var (installed, output) = await fixture.RunAsync(
            "install", "--from", fixture.Staged, "--tag", fixture.Tag,
            "--install-dir", fixture.InstallDirectory, "--no-path", "--no-agents");
        Assert.True(installed == 0, output);

        fixture.Publish("v0.9.3");
        var before = fixture.Fingerprint();

        var (exitCode, report) = await fixture.RunAsync(
            "update", "--check", "--install-dir", fixture.InstallDirectory, "--no-path");

        Assert.True(exitCode == 0, report);
        Assert.Contains($"Installed {fixture.Tag}, available v0.9.3.", report, StringComparison.Ordinal);
        // Nothing was written, and that is checked against the files rather than against the report's words.
        Assert.Equal(before, fixture.Fingerprint());
    }

    [Fact]
    public async Task An_update_replaces_the_installation_with_the_release_the_feed_publishes()
    {
        using var fixture = new CliInstallationFixture();
        fixture.Stage();
        var (installed, output) = await fixture.RunAsync(
            "install", "--from", fixture.Staged, "--tag", "v0.9.2",
            "--install-dir", fixture.InstallDirectory, "--no-path", "--no-agents");
        Assert.True(installed == 0, output);

        fixture.Publish("v0.9.3");

        var (exitCode, report) = await fixture.RunAsync(
            "update", "--install-dir", fixture.InstallDirectory, "--no-path", "--no-agents");

        Assert.True(exitCode == 0, report);
        Assert.Equal("v0.9.3", InstalledVersionFile.Read(fixture.InstallDirectory)?.Tag);
        Assert.Contains("Updated v0.9.2 to v0.9.3", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// A temporary installation, its data directory, its feed, and the staging directory the script would fill.
    /// </summary>
    private sealed class CliInstallationFixture : IDisposable
    {
        private const string ReleaseContent = "the release";

        public CliInstallationFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"aiko-cli-installation-{Guid.NewGuid():N}");
            DatabasePath = Path.Combine(Root, "data", "aiko.db");
            Home = Path.Combine(Root, "home");
            InstallDirectory = Path.Combine(Root, "bin");
            Staged = Path.Combine(Root, "bin.staging-20260101000000000");
            Feed = Path.Combine(Root, "feed");
            Directory.CreateDirectory(Home);
        }

        public string Root { get; }

        public string DatabasePath { get; }

        public string Home { get; }

        public string InstallDirectory { get; }

        /// <summary>Where the bootstrap script unpacks a release: beside the installation, never inside it.</summary>
        public string Staged { get; }

        public string Feed { get; }

        public string Tag => "v0.9.2";

        /// <summary>Unpacks a release's promised layout into the staging directory, as the script does.</summary>
        public void Stage()
        {
            foreach (var entry in ReleaseLayout.RequiredEntries)
            {
                var path = Path.Combine(Staged, entry);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, ReleaseContent);
            }
        }

        /// <summary>Publishes that layout as a release of a local feed, so an update needs no network.</summary>
        /// <param name="tag">Tag to publish, and the one the feed then considers current.</param>
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
                    writer.Write(ReleaseContent);
                }
            }

            File.WriteAllText(Path.Combine(Feed, LocalReleaseSource.LatestFileName), tag);
            File.WriteAllText(
                Path.Combine(releaseDirectory, ReleaseNaming.ChecksumsAssetName),
                $"{Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(archivePath)))}  {assetName}");
        }

        /// <summary>Runs the built CLI with this fixture's data directory, home and feed.</summary>
        public Task<(int ExitCode, string Output)> RunAsync(params string[] arguments) =>
            CliInitSpecs.RunCliWithEnvironmentAsync(
                DatabasePath,
                Home,
                new Dictionary<string, string> { ["AIKO_RELEASE_FEED"] = Feed },
                arguments);

        /// <summary>
        /// Every file of the installation by name and content. Comparing two of these is how "nothing was
        /// written" is shown without trusting the report that says so.
        /// </summary>
        public string Fingerprint()
        {
            var builder = new System.Text.StringBuilder();
            if (!Directory.Exists(InstallDirectory))
            {
                return string.Empty;
            }

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
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
