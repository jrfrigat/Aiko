using System.IO.Compression;
using Aiko.Infrastructure.Storage;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// The archive a project travels in: what the backup writes, what the restore reads, and what a restore
/// must refuse.
/// </summary>
public sealed class ProjectArchiveSpecs
{
    [Fact]
    public async Task An_archive_holds_the_contents_of_the_directory_and_says_what_it_holds()
    {
        var root = NewDirectory();
        var source = Path.Combine(root, ".aiko");
        var archive = Path.Combine(root, "backup.zip");

        try
        {
            Directory.CreateDirectory(Path.Combine(source, "workflows"));
            await File.WriteAllTextAsync(Path.Combine(source, "settings.json"), "{\"schemaVersion\":1}");
            await File.WriteAllTextAsync(Path.Combine(source, "workflows", "epic.json"), "{\"id\":\"epic\"}");

            var summary = await ProjectArchive.CreateAsync(source, archive, CancellationToken.None);

            Assert.True(File.Exists(archive));
            Assert.Equal(archive, summary.Path);
            // The contents, not the directory itself: unpacking it back into .aiko is what a restore does.
            using var opened = ZipFile.OpenRead(archive);
            Assert.Equal(2, summary.Entries);
            Assert.Equal(
                ["settings.json", "workflows/epic.json"],
                opened.Entries.Select(entry => entry.FullName).OrderBy(name => name, StringComparer.Ordinal));
            Assert.True(summary.Bytes > 0);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task Unpacking_returns_the_files_and_overwrites_the_names_it_finds()
    {
        var root = NewDirectory();
        var source = Path.Combine(root, ".aiko");
        var archive = Path.Combine(root, "backup.zip");
        var restored = Path.Combine(root, "restored", ".aiko");

        try
        {
            Directory.CreateDirectory(source);
            await File.WriteAllTextAsync(Path.Combine(source, "settings.json"), "the backed up document");
            await ProjectArchive.CreateAsync(source, archive, CancellationToken.None);

            var written = await ProjectArchive.ExtractAsync(archive, restored, CancellationToken.None);

            Assert.Equal(1, written);
            Assert.Equal(
                "the backed up document",
                await File.ReadAllTextAsync(Path.Combine(restored, "settings.json")));

            // A restore lands on a directory that already has files: the archive wins, by name.
            await File.WriteAllTextAsync(Path.Combine(restored, "settings.json"), "something newer");
            await File.WriteAllTextAsync(Path.Combine(restored, "extra.json"), "not in the archive");
            await ProjectArchive.ExtractAsync(archive, restored, CancellationToken.None);
            Assert.Equal(
                "the backed up document",
                await File.ReadAllTextAsync(Path.Combine(restored, "settings.json")));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task An_entry_that_would_escape_the_target_is_refused_before_anything_is_written()
    {
        var root = NewDirectory();
        var archive = Path.Combine(root, "hostile.zip");
        var restored = Path.Combine(root, "project", ".aiko");
        var escaped = Path.Combine(root, "project", "escaped.txt");

        try
        {
            Directory.CreateDirectory(root);
            using (var created = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                // Written first, so a naive unpacker would have it on disk before reaching the hostile one.
                WriteEntry(created, "settings.json", "harmless");
                WriteEntry(created, "../escaped.txt", "written where the archive should not reach");
            }

            var exception = await Assert.ThrowsAsync<InvalidDataException>(
                () => ProjectArchive.ExtractAsync(archive, restored, CancellationToken.None));

            Assert.Contains("../escaped.txt", exception.Message, StringComparison.Ordinal);
            // Nothing outside the target, and nothing inside it either: the entry list is checked before the
            // first byte is written, because a half-applied hostile archive is its own kind of damage.
            Assert.False(File.Exists(escaped));
            Assert.False(File.Exists(Path.Combine(restored, "settings.json")));
        }
        finally
        {
            Delete(root);
        }
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        using var stream = archive.CreateEntry(name).Open();
        using var writer = new StreamWriter(stream);
        writer.Write(content);
    }

    private static string NewDirectory() =>
        Path.Combine(Path.GetTempPath(), "Aiko.Specs", Guid.NewGuid().ToString("N"));

    private static void Delete(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
