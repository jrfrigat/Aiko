using Aiko.Application.Installation;
using Aiko.Infrastructure.Installation;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// Putting a staged release in place of what is installed. The interesting cases are the ones where
/// something went wrong: a replacement that died half-way, a failure while the version is recorded, and a
/// <c>previous</c> directory whose two meanings must not be confused.
/// </summary>
public sealed class InstallationReplacementSpecs
{
    [Fact]
    public void A_staged_release_replaces_what_is_installed()
    {
        using var fixture = new ReplacementFixture();
        fixture.Install("aiko.exe", "old-only.txt");
        var staged = fixture.Stage("aiko.exe", "new-only.txt");

        var result = fixture.Replacement.Apply(staged, fixture.InstallDirectory, updatePath: false);

        Assert.Equal("v0.9.2", result.Version.Tag);
        Assert.Equal("0.9.2", result.Version.Version);
        Assert.Equal(2, result.EntriesReplaced);
        Assert.False(result.RecoveredPrevious);

        var installed = fixture.InstalledNames();
        Assert.Contains("aiko.exe", installed);
        Assert.Contains("new-only.txt", installed);
        Assert.Contains(InstallationFiles.VersionFileName, installed);
        Assert.DoesNotContain("old-only.txt", installed);
        Assert.Empty(fixture.PreviousDirectories());
    }

    [Theory]
    [InlineData("some-other-tool.exe")]
    [InlineData("aiko.db")]
    public void A_directory_that_holds_something_other_than_aiko_is_refused_and_left_as_it_was(string foreign)
    {
        // -InstallDir D:\Tools, or the data directory by mistake: the directory is not Aiko's to empty.
        using var fixture = new ReplacementFixture();
        fixture.Install(foreign);
        var staged = fixture.Stage(ReleaseLayout.CliFileName);

        var refused = Assert.Throws<InstallationRefusedException>(() =>
            fixture.Replacement.Apply(staged, fixture.InstallDirectory, updatePath: false));

        Assert.Contains(fixture.InstallDirectory, refused.Message, StringComparison.Ordinal);
        Assert.Equal([foreign], fixture.InstalledNames());
        Assert.Empty(fixture.PreviousDirectories());
    }

    [Fact]
    public void An_empty_directory_and_an_existing_installation_are_both_the_installers_to_fill()
    {
        Assert.Null(InstallationReplacement.DescribeForeignContent(
            Path.Combine(Path.GetTempPath(), "Aiko.Specs", Guid.NewGuid().ToString("N"))));

        using var fixture = new ReplacementFixture();
        Assert.Null(InstallationReplacement.DescribeForeignContent(fixture.InstallDirectory));
        fixture.Install(InstallationFiles.VersionFileName, "whatever.dll");
        Assert.Null(InstallationReplacement.DescribeForeignContent(fixture.InstallDirectory));
    }

    [Fact]
    public void The_previous_version_comes_back_when_the_record_cannot_be_written()
    {
        using var fixture = new ReplacementFixture();
        fixture.Install("aiko.exe", "old-only.txt");

        // A directory where the temporary record file goes: the write fails after the entries are already in
        // place, which is the moment the rollback exists for.
        var staged = fixture.Stage("aiko.exe", "new-only.txt", "dir:install.json.tmp");

        Assert.ThrowsAny<Exception>(() =>
            fixture.Replacement.Apply(staged, fixture.InstallDirectory, updatePath: false));

        Assert.Equal(["aiko.exe", "old-only.txt"], fixture.InstalledNames());
        Assert.False(File.Exists(Path.Combine(fixture.InstallDirectory, InstallationFiles.VersionFileName)));
        Assert.Empty(fixture.PreviousDirectories());
    }

    [Fact]
    public void An_interrupted_replacement_is_repaired_by_the_next_run()
    {
        using var fixture = new ReplacementFixture();
        fixture.Install("leftover.txt");
        fixture.LeavePrevious("aiko.exe", "the-old-version.txt");

        var result = fixture.Replacement.Apply(
            fixture.Stage("aiko.exe", "the-new-version.txt"),
            fixture.InstallDirectory,
            updatePath: false);

        // The previous version was put back first, and then replaced by this run - and the caller is told, so
        // a repair does not look like an installation that was fine all along.
        Assert.True(result.RecoveredPrevious);
        var installed = fixture.InstalledNames();
        Assert.Contains("aiko.exe", installed);
        Assert.Contains("the-new-version.txt", installed);
        Assert.DoesNotContain("leftover.txt", installed);
        Assert.Empty(fixture.PreviousDirectories());
    }

    [Fact]
    public void A_previous_directory_beside_a_whole_installation_is_discarded_not_restored()
    {
        using var fixture = new ReplacementFixture();
        fixture.Install(ReleaseLayout.CliFileName);
        fixture.LeavePrevious(ReleaseLayout.CliFileName, "the-old-version.txt");

        var result = fixture.Replacement.Apply(
            fixture.Stage(ReleaseLayout.CliFileName, "the-new-version.txt"),
            fixture.InstallDirectory,
            updatePath: false);

        // Restoring here would undo a finished update, so the leftover goes and nothing is reported as
        // repaired.
        Assert.False(result.RecoveredPrevious);
        Assert.Empty(fixture.PreviousDirectories());
        Assert.DoesNotContain("the-old-version.txt", fixture.InstalledNames());
    }

    [Fact]
    public void The_installed_version_is_written_and_read_back()
    {
        using var fixture = new ReplacementFixture();
        var staged = fixture.Stage(ReleaseLayout.CliFileName);

        var result = fixture.Replacement.Apply(staged, fixture.InstallDirectory, updatePath: false);
        var read = InstalledVersionFile.Read(fixture.InstallDirectory);

        Assert.NotNull(read);
        Assert.Equal(result.Version.Tag, read.Tag);
        Assert.Equal(result.Version.Version, read.Version);
        Assert.Equal(InstalledVersion.LatestChannel, read.Channel);
        Assert.Equal(fixture.InstallDirectory, read.InstallDirectory);
    }

    [Fact]
    public void An_installation_without_a_version_file_reports_nothing_rather_than_a_guess()
    {
        using var fixture = new ReplacementFixture();
        fixture.Install(ReleaseLayout.CliFileName);

        Assert.Null(InstalledVersionFile.Read(fixture.InstallDirectory));
    }

    [Theory]
    [InlineData(null, @"C:\Aiko\bin", true)]
    [InlineData(@"C:\Windows", @"C:\Aiko\bin", true)]
    [InlineData(@"C:\Aiko\bin;C:\Windows", @"C:\Aiko\bin", false)]
    [InlineData(@"C:\AIKO\BIN\;C:\Windows", @"C:\Aiko\bin", false)]
    [InlineData(@"C:\Windows;C:\Aiko\bin\", @"C:\Aiko\bin", false)]
    public void A_path_entry_is_added_only_when_the_directory_is_not_named(
        string? pathValue,
        string directory,
        bool expectedAdded)
    {
        var updated = InstallationPath.AddPathEntry(pathValue, directory, out var added);

        Assert.Equal(expectedAdded, added);
        if (!expectedAdded)
        {
            Assert.Equal(pathValue ?? string.Empty, updated);
        }
        else
        {
            Assert.Contains(directory, updated, StringComparison.Ordinal);
        }
    }

    /// <summary>A staged release and an installation this spec owns, deleted on dispose.</summary>
    private sealed class ReplacementFixture : IDisposable
    {
        public ReplacementFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "Aiko.Specs", Guid.NewGuid().ToString("N"));
            InstallDirectory = Path.Combine(Root, "bin");
            StagedDirectory = Path.Combine(Root, "staging");
            Directory.CreateDirectory(InstallDirectory);
            Directory.CreateDirectory(StagedDirectory);
        }

        public string Root { get; }

        public string InstallDirectory { get; }

        public string StagedDirectory { get; }

        public InstallationReplacement Replacement { get; } = new();

        /// <summary>Puts entries into the installation.</summary>
        public void Install(params string[] entries) => Write(InstallDirectory, entries);

        /// <summary>Builds a staged release holding these entries.</summary>
        public StagedRelease Stage(params string[] entries)
        {
            Write(StagedDirectory, entries);
            return new StagedRelease(
                "v0.9.2",
                "0.9.2",
                "aiko-0.9.2-win-x64.zip",
                new string('a', 64),
                StagedDirectory);
        }

        /// <summary>Leaves a previous version beside the installation, as an interrupted run would.</summary>
        public void LeavePrevious(params string[] entries)
        {
            var previous = $"{InstallDirectory}.previous-20260921120000000";
            Directory.CreateDirectory(previous);
            Write(previous, entries);
        }

        /// <summary>What the installation holds, by name.</summary>
        public string[] InstalledNames() =>
            Directory.GetFileSystemEntries(InstallDirectory)
                .Select(Path.GetFileName)
                .OfType<string>()
                .Order(StringComparer.Ordinal)
                .ToArray();

        public string[] PreviousDirectories() => Directory.GetDirectories(Root, "bin.previous-*");

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }

        private static void Write(string directory, IReadOnlyList<string> entries)
        {
            foreach (var entry in entries)
            {
                // `dir:` marks an entry that has to be a directory rather than a file: one spec needs a
                // directory exactly where the temporary record file is written, so that write fails.
                var asDirectory = entry.StartsWith("dir:", StringComparison.Ordinal);
                var name = asDirectory ? entry[4..] : entry;
                var path = Path.Combine(directory, name);
                if (asDirectory)
                {
                    Directory.CreateDirectory(path);
                }
                else
                {
                    File.WriteAllText(path, name);
                }
            }
        }
    }
}
