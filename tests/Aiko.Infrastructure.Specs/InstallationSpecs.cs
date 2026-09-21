using Aiko.Infrastructure.Storage;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// Taking an installation back off a machine. Three things are separate - the PATH entry, the published
/// binaries and the data - and the data is the one that is asked about by name, because it holds every
/// project's registration and the projects' own .aiko directories hold their work.
/// </summary>
public sealed class InstallationSpecs
{
    [Fact]
    public void A_path_entry_goes_and_the_rest_of_the_path_keeps_its_order()
    {
        const string bin = @"C:\Users\someone\AppData\Local\Aiko\bin";

        var edited = AikoInstallation.RemovePathEntry(
            $@"C:\Windows;{bin};D:\tools",
            bin,
            out var removed);
        Assert.True(removed);
        Assert.Equal(@"C:\Windows;D:\tools", edited);

        // The same directory spelled with a trailing separator or in another case is still the same
        // directory on Windows, and either spelling may be the one in the value.
        AikoInstallation.RemovePathEntry($@"C:\Windows;{bin}\", bin, out var withSeparator);
        Assert.True(withSeparator);

        AikoInstallation.RemovePathEntry(
            @"C:\Windows;C:\Users\SOMEONE\AppData\Local\AIKO\BIN;D:\tools",
            bin,
            out var otherCase);
        Assert.True(otherCase);

        // A machine where the entry was never added - or where a second uninstall runs - is left as it is.
        var untouched = AikoInstallation.RemovePathEntry(@"C:\Windows;D:\tools", bin, out var nothingRemoved);
        Assert.False(nothingRemoved);
        Assert.Equal(@"C:\Windows;D:\tools", untouched);

        // The installer prepends, so the entry may also be the first one.
        Assert.Equal(
            @"C:\Windows;D:\tools",
            AikoInstallation.RemovePathEntry($@"{bin};C:\Windows;D:\tools", bin, out _));
    }

    [Fact]
    public void Removing_the_binaries_reports_the_files_that_are_still_in_use()
    {
        var dataDirectory = NewDirectory();
        var installation = new AikoInstallation(new AikoDataPaths(Path.Combine(dataDirectory, "aiko.db")));
        Directory.CreateDirectory(installation.BinDirectory);
        var removable = Path.Combine(installation.BinDirectory, "aiko-stdio.exe");
        var inUse = Path.Combine(installation.BinDirectory, "aiko.exe");
        File.WriteAllText(removable, "binary");
        File.WriteAllText(inUse, "binary");

        try
        {
            // The running program holds its own file, which is exactly what an uninstall started from the
            // installed CLI runs into.
            using (new FileStream(inUse, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var removal = installation.RemoveBinaries();

                Assert.True(removal.Removed);
                Assert.Equal([inUse], removal.Blocked);
                Assert.False(File.Exists(removable));
                Assert.True(File.Exists(inUse));
            }

            // Once nothing holds it, the same call finishes the job - which is what the command tells the
            // user to do after it exits.
            var second = installation.RemoveBinaries();
            Assert.True(second.Removed);
            Assert.Empty(second.Blocked);
            Assert.False(Directory.Exists(installation.BinDirectory));

            // A third call finds nothing at all, and that is not a failure.
            var third = installation.RemoveBinaries();
            Assert.False(third.Removed);
        }
        finally
        {
            Delete(dataDirectory);
        }
    }

    [Fact]
    public void Removing_the_data_takes_that_directory_and_leaves_the_projects_alone()
    {
        var root = NewDirectory();
        var dataDirectory = Path.Combine(root, "Aiko");
        var projectRoot = Path.Combine(root, "project");
        var projectData = AikoProjectPaths.DataRoot(projectRoot);
        var installation = new AikoInstallation(new AikoDataPaths(Path.Combine(dataDirectory, "aiko.db")));

        try
        {
            Directory.CreateDirectory(dataDirectory);
            File.WriteAllText(Path.Combine(dataDirectory, "aiko.db"), "database");
            File.WriteAllText(Path.Combine(dataDirectory, "settings.json"), "{}");
            Directory.CreateDirectory(Path.Combine(dataDirectory, "backups"));
            File.WriteAllText(Path.Combine(dataDirectory, "backups", "aiko-1.zip"), "archive");

            Directory.CreateDirectory(projectData);
            File.WriteAllText(Path.Combine(projectData, "relations.json"), "{}");
            File.WriteAllText(Path.Combine(projectRoot, "README.md"), "the user's own file");

            var removal = installation.RemoveData();

            Assert.True(removal.Removed);
            Assert.Empty(removal.Blocked);
            Assert.False(Directory.Exists(dataDirectory));

            // The project's own .aiko is a separate question with a separate flag: removing this
            // installation's data must not touch the work of any project.
            Assert.True(File.Exists(Path.Combine(projectData, "relations.json")));
            Assert.True(File.Exists(Path.Combine(projectRoot, "README.md")));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Removing_a_projects_own_metadata_leaves_the_rest_of_the_project()
    {
        var root = NewDirectory();
        var projectRoot = Path.Combine(root, "project");
        var installation = new AikoInstallation(new AikoDataPaths(Path.Combine(root, "Aiko", "aiko.db")));

        try
        {
            var projectData = AikoProjectPaths.DataRoot(projectRoot);
            Directory.CreateDirectory(Path.Combine(projectData, "stories", "STORY-1"));
            File.WriteAllText(Path.Combine(projectData, "relations.json"), "{}");
            Directory.CreateDirectory(Path.Combine(projectRoot, "src"));
            File.WriteAllText(Path.Combine(projectRoot, "src", "Program.cs"), "code");

            var removal = installation.RemoveProjectData(projectRoot);

            Assert.True(removal.Removed);
            Assert.Empty(removal.Blocked);
            Assert.False(Directory.Exists(projectData));

            // Only .aiko goes: the source tree beside it is the project, not Aiko's copy of its notes.
            Assert.True(File.Exists(Path.Combine(projectRoot, "src", "Program.cs")));

            // A project that never had .aiko, or a second call, reports nothing to remove.
            Assert.False(installation.RemoveProjectData(projectRoot).Removed);
        }
        finally
        {
            Delete(root);
        }
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
