using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Aiko.Application.Installation;
using Aiko.Infrastructure.Installation;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// The whole path a person walks: one install, then one update, both against a feed that is a directory - no
/// network, no GitHub release, and no installation of the owner's.
/// </summary>
/// <remarks>
/// What this set is for is the one property the smaller ones cannot show: an install and an update replace the
/// binaries and leave <b>everything else</b> exactly as it was. The installation's own data
/// (<c>settings.json</c>, <c>app-settings.json</c>, <c>access-token</c>, <c>backups/</c>) and the project - its
/// <c>.aiko</c> included - are fingerprinted before and after each run. That matters because a run's repair
/// step walks every registered project (<see cref="InstallationRepair"/>), so the code that could rewrite a
/// project is on this path, and nothing else in the suite would notice if it did.
/// <para>
/// The SQLite database is deliberately <b>not</b> fingerprinted: the reindex rebuilds the projections and
/// writes the database by definition. What stands in for it is the registration being found afterwards and the
/// repair naming the project it reindexed - a projection rebuilt from <c>.aiko</c> rather than lost with it.
/// </para>
/// </remarks>
public sealed class LocalFeedInstallationSpecs
{
    [Fact]
    public async Task An_install_and_an_update_on_a_local_feed_leave_the_data_and_the_projects_untouched()
    {
        using var fixture = new InstalledDataFixture();
        fixture.Stage();

        var (initExitCode, initOutput) = await fixture.RunAsync(
            "init", fixture.ProjectRoot, "--name", InstalledDataFixture.ProjectName);
        Assert.True(initExitCode == 0, initOutput);
        Assert.True(Directory.Exists(Path.Combine(fixture.ProjectRoot, ".aiko")), initOutput);

        var pathBefore = fixture.UserPath();
        var beforeInstall = fixture.Fingerprint();

        // What the fingerprint watches, said out loud: a fingerprint that named nothing would compare equal
        // to itself whatever a run did, so this is the difference between a check and a formality.
        Assert.Contains("project/.aiko/", beforeInstall, StringComparison.Ordinal);
        Assert.Contains("project/.gitignore", beforeInstall, StringComparison.Ordinal);
        Assert.Contains("data|", beforeInstall, StringComparison.Ordinal);
        Assert.Contains("backups/aiko-20260101.zip", beforeInstall, StringComparison.Ordinal);

        var (installExitCode, installOutput) = await fixture.RunAsync(
            "install", "--from", fixture.Staged,
            "--tag", InstalledDataFixture.FirstTag,
            "--install-dir", fixture.InstallDirectory,
            "--no-path", "--no-agents");

        Assert.True(installExitCode == 0, installOutput);
        Assert.Equal(InstalledDataFixture.FirstTag, InstalledVersionFile.Read(fixture.InstallDirectory)?.Tag);

        // The repair reports each project it reindexed. Without this line the fingerprint below would pass just
        // as well over a project nobody looked at, which is the one way the test could lie.
        Assert.Contains("reindex:", installOutput, StringComparison.Ordinal);
        Assert.Contains($"{InstalledDataFixture.ProjectName}: ", installOutput, StringComparison.Ordinal);

        // The install replaced the binaries and wrote nothing else: not the data, not the project, and - with
        // --no-path - not the user's PATH.
        Assert.Equal(beforeInstall, fixture.Fingerprint());
        Assert.Equal(pathBefore, fixture.UserPath());

        // The update: the feed publishes one more release, and the installed CLI takes it.
        fixture.Publish(InstalledDataFixture.SecondTag);
        var beforeUpdate = fixture.Fingerprint();

        var (updateExitCode, updateOutput) = await fixture.RunAsync(
            "update", "--install-dir", fixture.InstallDirectory, "--no-path", "--no-agents");

        Assert.True(updateExitCode == 0, updateOutput);
        Assert.Contains(
            $"Updated {InstalledDataFixture.FirstTag} to {InstalledDataFixture.SecondTag}",
            updateOutput,
            StringComparison.Ordinal);
        Assert.Equal(InstalledDataFixture.SecondTag, InstalledVersionFile.Read(fixture.InstallDirectory)?.Tag);

        // The other half of the promise, and the one the story's own criterion is about: the new release is in
        // place, and what surrounded it is byte for byte what it was.
        Assert.Equal(beforeUpdate, fixture.Fingerprint());
        Assert.Equal(pathBefore, fixture.UserPath());

        // The registration still resolves, which is the database's half of "the data is untouched".
        var (findExitCode, findOutput) = await fixture.RunAsync("project", "find", fixture.ProjectRoot);
        Assert.True(findExitCode == 0, findOutput);
        Assert.Contains(fixture.ProjectRoot, findOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_report_only_update_writes_nothing_to_the_data_or_the_project()
    {
        using var fixture = new InstalledDataFixture();
        fixture.Stage();
        var (initExitCode, initOutput) = await fixture.RunAsync(
            "init", fixture.ProjectRoot, "--name", InstalledDataFixture.ProjectName);
        Assert.True(initExitCode == 0, initOutput);

        var (installExitCode, installOutput) = await fixture.RunAsync(
            "install", "--from", fixture.Staged,
            "--tag", InstalledDataFixture.FirstTag,
            "--install-dir", fixture.InstallDirectory,
            "--no-path", "--no-agents");
        Assert.True(installExitCode == 0, installOutput);

        fixture.Publish(InstalledDataFixture.SecondTag);
        var before = fixture.Fingerprint();

        var (exitCode, output) = await fixture.RunAsync(
            "update", "--check", "--install-dir", fixture.InstallDirectory, "--no-path");

        Assert.True(exitCode == 0, output);
        Assert.Contains(
            $"Installed {InstalledDataFixture.FirstTag}, available {InstalledDataFixture.SecondTag}.",
            output,
            StringComparison.Ordinal);
        // `--check` stops before anything is fetched, stopped, replaced or repaired, so nothing outside the
        // installation may have moved either.
        Assert.Equal(before, fixture.Fingerprint());
    }

    /// <summary>
    /// An installation, its data directory, its feed and the project it works on - all of it this spec's own,
    /// under one temporary directory that dispose removes.
    /// </summary>
    private sealed class InstalledDataFixture : IDisposable
    {
        /// <summary>The release the installation is made with.</summary>
        public const string FirstTag = "v0.9.2";

        /// <summary>The release the feed publishes afterwards, which the update has to take.</summary>
        public const string SecondTag = "v0.9.3";

        /// <summary>Name given to the project, so the repair's report can be read back.</summary>
        public const string ProjectName = "Sample";

        private const string ReleaseContent = "the release";

        public InstalledDataFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"aiko-local-feed-{Guid.NewGuid():N}");
            DataDirectory = Path.Combine(Root, "data");
            DatabasePath = Path.Combine(DataDirectory, "aiko.db");
            Home = Path.Combine(Root, "home");
            ProjectRoot = Path.Combine(Root, "project");
            InstallDirectory = Path.Combine(Root, "bin");
            Staged = Path.Combine(Root, "bin.staging-20260101000000000");
            Feed = Path.Combine(Root, "feed");

            Directory.CreateDirectory(Home);
            Directory.CreateDirectory(ProjectRoot);
            Directory.CreateDirectory(Feed);

            // What an installation that has been in use for a while holds: the daemon's own settings, the token
            // every client reads and an archive in the backups. None of it belongs to the release being
            // installed, and none of it may be written, moved or dropped by one.
            Directory.CreateDirectory(DataDirectory);
            File.WriteAllText(Path.Combine(DataDirectory, "settings.json"), "{\"port\":24560}");
            File.WriteAllText(Path.Combine(DataDirectory, "app-settings.json"), "{\"priority\":{}}");
            File.WriteAllText(Path.Combine(DataDirectory, "access-token"), "the-access-token");
            Directory.CreateDirectory(Path.Combine(DataDirectory, "backups"));
            File.WriteAllText(Path.Combine(DataDirectory, "backups", "aiko-20260101.zip"), "an archive");
        }

        public string Root { get; }

        public string DataDirectory { get; }

        public string DatabasePath { get; }

        public string Home { get; }

        public string ProjectRoot { get; }

        public string InstallDirectory { get; }

        /// <summary>Where the bootstrap script unpacks a release: beside the installation, never inside it.</summary>
        public string Staged { get; }

        public string Feed { get; }

        /// <summary>
        /// Unpacks a release's promised layout into the staging directory, which is what
        /// <c>scripts/install.ps1</c> does before it hands the directory to the CLI.
        /// </summary>
        public void Stage()
        {
            foreach (var entry in ReleaseLayout.RequiredEntries)
            {
                var path = Path.Combine(Staged, entry);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, ReleaseContent);
            }
        }

        /// <summary>Publishes the same layout as a release of the local feed, and makes it the current one.</summary>
        /// <param name="tag">Tag to publish, which the feed then considers current.</param>
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

        /// <summary>
        /// Everything a run must not touch, by name and content: the project whole, the installation's own data
        /// files, and everything under <c>backups</c>.
        /// </summary>
        /// <remarks>
        /// Comparing two of these is how "nothing was written" is checked against the disk rather than against
        /// the report that claims it. The database is left out on purpose - see this type's own remarks.
        /// </remarks>
        public string Fingerprint()
        {
            var builder = new StringBuilder();
            AppendTree(builder, ProjectRoot, "project");
            foreach (var name in new[] { "settings.json", "app-settings.json", "access-token" })
            {
                AppendFile(builder, Path.Combine(DataDirectory, name), "data");
            }

            AppendTree(builder, Path.Combine(DataDirectory, "backups"), "backups");
            return builder.ToString();
        }

        /// <summary>The user PATH as a process started now would read it. This spec only ever reads it.</summary>
        public string? UserPath() =>
            Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User);

        /// <summary>Runs the built CLI with this fixture's data directory, home and feed.</summary>
        public Task<(int ExitCode, string Output)> RunAsync(params string[] arguments) =>
            CliInitSpecs.RunCliWithEnvironmentAsync(
                DatabasePath,
                Home,
                new Dictionary<string, string> { ["AIKO_RELEASE_FEED"] = Feed },
                arguments);

        public void Dispose()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private static void AppendTree(StringBuilder builder, string directory, string label)
        {
            if (!Directory.Exists(directory))
            {
                builder.Append(label).Append("|missing\n");
                return;
            }

            foreach (var file in Directory
                .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal))
            {
                AppendFile(builder, file, $"{label}/{Path.GetRelativePath(directory, file).Replace('\\', '/')}");
            }
        }

        private static void AppendFile(StringBuilder builder, string path, string label)
        {
            builder.Append(label).Append('|');
            builder.Append(File.Exists(path)
                ? Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)))
                : "missing");
            builder.Append('\n');
        }
    }
}

