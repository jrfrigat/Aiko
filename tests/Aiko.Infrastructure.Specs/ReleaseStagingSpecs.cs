using System.IO.Compression;
using System.Security.Cryptography;
using Aiko.Application.Installation;
using Aiko.Infrastructure.Installation;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// Preparing a release beside the installation. Everything runs against a local feed and temporary
/// directories, so the properties the story asks for - the previous version is untouched, a failed attempt
/// leaves nothing behind - are shown without a network and without going near the owner's installation.
/// </summary>
public sealed class ReleaseStagingSpecs
{
    [Fact]
    public async Task A_published_release_is_staged_and_the_installation_is_untouched()
    {
        using var fixture = new StagingFixture();
        fixture.Publish("v0.9.2", StagingFixture.CompleteLayout());

        var staged = await new ReleaseStaging(fixture.Source)
            .PrepareAsync("v0.9.2", fixture.InstallDirectory, CancellationToken.None);

        Assert.Equal("v0.9.2", staged.Tag);
        Assert.Equal("0.9.2", staged.Version);
        Assert.Equal("aiko-0.9.2-win-x64.zip", staged.AssetName);
        Assert.Equal(64, staged.Sha256.Length);
        foreach (var entry in ReleaseLayout.RequiredEntries)
        {
            Assert.True(File.Exists(Path.Combine(staged.Directory, entry)), entry);
        }

        // The archive is gone, and the installation still holds exactly what it held.
        Assert.Empty(Directory.GetFiles(fixture.Root, "*.staging-*.zip"));
        Assert.Equal(["aiko.exe"], InstalledFiles(fixture));
    }

    [Fact]
    public async Task An_archive_that_does_not_match_its_checksum_is_refused_and_leaves_nothing_behind()
    {
        using var fixture = new StagingFixture();
        fixture.Publish("v0.9.2", StagingFixture.CompleteLayout(), corruptChecksum: true);

        var refusal = await Assert.ThrowsAsync<InstallationRefusedException>(async () =>
            await new ReleaseStaging(fixture.Source)
                .PrepareAsync("v0.9.2", fixture.InstallDirectory, CancellationToken.None));

        Assert.Contains("checksum", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetDirectories(fixture.Root, "*.staging-*"));
        Assert.Empty(Directory.GetFiles(fixture.Root, "*.staging-*.zip"));
        Assert.Equal(["aiko.exe"], InstalledFiles(fixture));
    }

    [Theory]
    [InlineData("aiko-stdio.exe")]
    [InlineData("server/Aiko.Server.exe")]
    [InlineData("server/wwwroot/index.html")]
    [InlineData("templates/default/template.json")]
    public async Task A_release_that_is_missing_a_required_entry_is_refused(string missing)
    {
        using var fixture = new StagingFixture();
        var entries = ReleaseLayout.RequiredEntries
            .Where(entry => !string.Equals(entry.Replace('\\', '/'), missing, StringComparison.Ordinal))
            .ToArray();
        fixture.Publish("v0.9.2", entries);

        var refusal = await Assert.ThrowsAsync<InstallationRefusedException>(async () =>
            await new ReleaseStaging(fixture.Source)
                .PrepareAsync("v0.9.2", fixture.InstallDirectory, CancellationToken.None));

        // The list is the release's own, so the stdio proxy and the template are required too - not only the
        // three entries a smaller list would have carried.
        Assert.Contains(missing.Replace('/', Path.DirectorySeparatorChar), refusal.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetDirectories(fixture.Root, "*.staging-*"));
        Assert.Equal(["aiko.exe"], InstalledFiles(fixture));
    }

    [Fact]
    public async Task A_release_that_vouches_for_nothing_is_refused_before_the_download()
    {
        using var fixture = new StagingFixture();
        fixture.PublishWithoutChecksumForAsset("v0.9.2");
        var counting = new CountingSource(fixture.Source);

        var refusal = await Assert.ThrowsAsync<InstallationRefusedException>(async () =>
            await new ReleaseStaging(counting)
                .PrepareAsync("v0.9.2", fixture.InstallDirectory, CancellationToken.None));

        Assert.Contains("no checksum", refusal.Message, StringComparison.OrdinalIgnoreCase);

        // The point of reading the checksums first: an archive nobody vouched for was never fetched.
        Assert.Equal(0, counting.Downloads);
        Assert.Equal(["aiko.exe"], InstalledFiles(fixture));
    }

    private static string[] InstalledFiles(StagingFixture fixture) =>
        Directory.GetFiles(fixture.InstallDirectory)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();

    /// <summary>A release feed and an installation this spec owns, deleted on dispose.</summary>
    private sealed class StagingFixture : IDisposable
    {
        public StagingFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "Aiko.Specs", Guid.NewGuid().ToString("N"));
            Feed = Path.Combine(Root, "feed");
            InstallDirectory = Path.Combine(Root, "bin");
            Directory.CreateDirectory(Feed);
            Directory.CreateDirectory(InstallDirectory);

            // A stand-in for the previous version: after a refusal the installation must be exactly this.
            File.WriteAllText(Path.Combine(InstallDirectory, "aiko.exe"), "the installed version");
        }

        public string Root { get; }

        public string Feed { get; }

        public string InstallDirectory { get; }

        public IReleaseSource Source => new LocalReleaseSource(Feed);

        /// <summary>The layout a real release carries, with the separators an archive uses.</summary>
        public static string[] CompleteLayout() =>
            ReleaseLayout.RequiredEntries.Select(entry => entry.Replace('\\', '/')).ToArray();

        /// <summary>Publishes a release: an archive with these entries, and its checksum.</summary>
        public void Publish(string tag, IReadOnlyList<string> entries, bool corruptChecksum = false)
        {
            var releaseDirectory = Path.Combine(Feed, tag);
            Directory.CreateDirectory(releaseDirectory);
            var assetName = ReleaseNaming.AssetNameForTag(tag);
            var archivePath = Path.Combine(releaseDirectory, assetName);

            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                foreach (var entry in entries)
                {
                    using var writer = new StreamWriter(archive.CreateEntry(entry).Open());
                    writer.Write(entry);
                }
            }

            WriteChecksums(
                releaseDirectory,
                assetName,
                corruptChecksum ? new string('0', 64) : Sha256Of(archivePath));
        }

        /// <summary>
        /// Publishes a release whose checksum file vouches for something else - the shape of a release that
        /// does not mention this archive at all.
        /// </summary>
        public void PublishWithoutChecksumForAsset(string tag)
        {
            Publish(tag, CompleteLayout());
            WriteChecksums(Path.Combine(Feed, tag), "aiko-9.9.9-win-x64.zip", new string('a', 64));
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }

        private static void WriteChecksums(string releaseDirectory, string assetName, string hash) =>
            File.WriteAllText(
                Path.Combine(releaseDirectory, ReleaseNaming.ChecksumsAssetName),
                $"{hash}  {assetName}");

        private static string Sha256Of(string path) =>
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    }

    /// <summary>A source that counts downloads, so "nothing was fetched" can be asserted.</summary>
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
            CancellationToken cancellationToken) => inner.ReadChecksumsAsync(tag, cancellationToken);
    }
}
