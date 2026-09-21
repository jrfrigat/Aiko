using Aiko.Application.Installation;
using Aiko.Infrastructure.Installation;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// Where a release comes from and what it is called. Both are checked without a network: the naming rules
/// are what a person types into a URL, and the local feed is what lets the install and update path be
/// exercised on a machine that has no internet.
/// </summary>
public sealed class ReleaseSourceSpecs
{
    [Theory]
    [InlineData("https://github.com/jrfrigat/Aiko/releases/tag/v0.9.2", "v0.9.2")]
    [InlineData("https://github.com/jrfrigat/Aiko/releases/tag/v0.9.2?foo=1", "v0.9.2")]
    [InlineData("https://github.com/jrfrigat/Aiko/releases/tag/v0.9.2#notes", "v0.9.2")]
    [InlineData("https://github.com/jrfrigat/Aiko/releases/tag/0.9.2+build.7", "0.9.2+build.7")]
    [InlineData("https://github.com/jrfrigat/Aiko/releases/tag/v1.0.0-rc.1", "v1.0.0-rc.1")]
    public void A_release_address_names_its_tag(string location, string expected) =>
        Assert.Equal(expected, GitHubReleaseSource.ParseTagFromLocation(location));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not an address")]
    [InlineData("https://github.com/jrfrigat/Aiko/releases")]
    [InlineData("https://github.com/jrfrigat/Aiko/releases/tag/")]
    [InlineData("https://evil.example.com/jrfrigat/Aiko/releases/tag/v1.0.0")]
    public void An_address_that_is_not_a_release_tag_is_refused(string? location) =>
        Assert.Null(GitHubReleaseSource.ParseTagFromLocation(location));

    [Theory]
    [InlineData("v0.9.2", "0.9.2")]
    [InlineData("V0.9.2", "0.9.2")]
    [InlineData("0.9.2", "0.9.2")]
    [InlineData("v0.9.2+build.7", "0.9.2")]
    [InlineData("v1.0.0-rc.1", "1.0.0-rc.1")]
    public void A_version_comes_from_the_tag(string tag, string expected) =>
        Assert.Equal(expected, ReleaseNaming.VersionFromTag(tag));

    [Theory]
    [InlineData("v")]
    [InlineData("v 1.0")]
    [InlineData("v1.0/../elsewhere")]
    [InlineData("v1.0\\..\\elsewhere")]
    public void A_tag_that_does_not_name_a_version_is_refused(string tag)
    {
        Assert.Throws<FormatException>(() =>
        {
            _ = ReleaseNaming.VersionFromTag(tag);
        });
    }

    [Fact]
    public void The_asset_is_named_after_the_version()
    {
        Assert.Equal("aiko-0.9.2-win-x64.zip", ReleaseNaming.AssetNameForTag("v0.9.2"));
        Assert.Equal("aiko-0.9.2-win-x64.zip", ReleaseNaming.AssetNameForTag("0.9.2"));
    }

    [Fact]
    public void Checksums_are_read_from_the_file_a_release_publishes()
    {
        var content = string.Join(
            '\n',
            $"{new string('a', 64)}  aiko-0.9.2-win-x64.zip",
            $"{new string('b', 64)} *aiko-installer.exe",
            $"{new string('c', 64)}\tSHA256SUMS",
            string.Empty,
            "   ");

        var checksums = ChecksumFile.Parse(content);

        Assert.Equal(3, checksums.Count);
        Assert.Equal(new string('a', 64), checksums["aiko-0.9.2-win-x64.zip"]);
        // The binary marker `sha256sum` writes is not part of the name.
        Assert.Equal(new string('b', 64), checksums["aiko-installer.exe"]);
        Assert.Equal(new string('c', 64), checksums["SHA256SUMS"]);
    }

    [Theory]
    [InlineData("only-a-name.zip")]
    [InlineData("deadbeef  aiko-0.9.2-win-x64.zip")]
    [InlineData("a b c")]
    public void A_checksum_line_that_is_not_a_hash_and_a_name_is_refused(string content)
    {
        Assert.Throws<FormatException>(() =>
        {
            _ = ChecksumFile.Parse(content);
        });
    }

    [Fact]
    public async Task The_local_feed_resolves_the_current_release_and_an_explicit_one()
    {
        using var feed = new ReleaseFeed();
        feed.Publish("v0.9.2");
        feed.SetCurrent("v0.9.2");
        var source = new LocalReleaseSource(feed.Root);

        // No tag named, "latest" named, and a tag named: the first two read the feed's own current release,
        // the third is taken as given.
        Assert.Equal("v0.9.2", await source.ResolveTagAsync(null, CancellationToken.None));
        Assert.Equal("v0.9.2", await source.ResolveTagAsync("latest", CancellationToken.None));
        Assert.Equal("v0.9.2", await source.ResolveTagAsync("v0.9.2", CancellationToken.None));
    }

    [Fact]
    public async Task A_feed_that_cannot_answer_says_so_instead_of_returning_nothing()
    {
        using var feed = new ReleaseFeed();
        var source = new LocalReleaseSource(feed.Root);

        // No latest.txt at all, and a tag the feed does not hold: both are refusals, not empty tags.
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await source.ResolveTagAsync(null, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await source.ResolveTagAsync("v9.9.9", CancellationToken.None));
    }

    [Fact]
    public async Task An_asset_and_its_checksums_are_read_from_the_feed()
    {
        using var feed = new ReleaseFeed();
        feed.Publish("v0.9.2");
        feed.SetCurrent("v0.9.2");
        var source = new LocalReleaseSource(feed.Root);
        var assetName = ReleaseNaming.AssetNameForTag("v0.9.2");

        var destination = Path.Combine(feed.Root, "downloaded.zip");
        await source.DownloadAssetAsync("v0.9.2", assetName, destination, CancellationToken.None);
        Assert.Equal("the archive", await File.ReadAllTextAsync(destination));

        var checksums = await source.ReadChecksumsAsync("v0.9.2", CancellationToken.None);
        Assert.Equal(new string('a', 64), checksums[assetName]);

        // An asset the feed does not hold is refused rather than copied from an empty path.
        await Assert.ThrowsAsync<FileNotFoundException>(
            async () => await source.DownloadAssetAsync(
                "v0.9.2",
                "aiko-9.9.9-win-x64.zip",
                destination,
                CancellationToken.None));
    }

    [Fact]
    public void A_tag_that_is_not_a_single_directory_name_is_refused()
    {
        var source = new LocalReleaseSource(Path.GetTempPath());

        // The tag names a directory inside the feed, so one that climbs out of it would read other files.
        Assert.Throws<FormatException>(() =>
        {
            _ = source.ResolveTagAsync("..\\elsewhere", CancellationToken.None);
        });
    }

    /// <summary>A release feed directory this spec owns, deleted on dispose.</summary>
    private sealed class ReleaseFeed : IDisposable
    {
        public ReleaseFeed()
        {
            Root = Path.Combine(Path.GetTempPath(), "Aiko.Specs", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        /// <summary>Publishes a release: its archive and the checksums that vouch for it.</summary>
        public void Publish(string tag)
        {
            var directory = Path.Combine(Root, tag);
            Directory.CreateDirectory(directory);

            var assetName = ReleaseNaming.AssetNameForTag(tag);
            File.WriteAllText(Path.Combine(directory, assetName), "the archive");
            File.WriteAllText(
                Path.Combine(directory, ReleaseNaming.ChecksumsAssetName),
                $"{new string('a', 64)}  {assetName}");
        }

        /// <summary>Writes which release the feed considers current.</summary>
        public void SetCurrent(string tag) =>
            File.WriteAllText(Path.Combine(Root, LocalReleaseSource.LatestFileName), tag);

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }
    }
}
