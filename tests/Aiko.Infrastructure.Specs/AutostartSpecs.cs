using Aiko.Infrastructure.Storage;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// Starting the daemon at sign-in: one command file in the user's own Startup folder, written only when it
/// is asked for and removed with the installation.
/// </summary>
public sealed class AutostartSpecs
{
    [Fact]
    public void Enabling_writes_the_entry_and_disabling_takes_it_away_again()
    {
        var root = NewDirectory();
        var startup = Path.Combine(root, "Startup");
        var executable = Path.Combine(root, "Aiko", "bin", "aiko.exe");
        var autostart = new AikoAutostart(executable, startup);

        try
        {
            // Nothing is written by merely constructing the object: the entry exists because Enable was
            // called, not because Aiko was installed.
            Assert.True(autostart.IsSupported);
            Assert.False(autostart.IsEnabled);
            Assert.False(File.Exists(autostart.EntryPath));

            autostart.Enable();

            Assert.True(autostart.IsEnabled);
            Assert.Equal(Path.Combine(startup, "aiko-autostart.cmd"), autostart.EntryPath);

            var written = File.ReadAllText(autostart.EntryPath);
            Assert.Equal(autostart.Body, written);
            // The entry has to name the installed command and the detached start: an entry that runs the
            // daemon in the foreground would leave a console open for the whole session.
            Assert.Contains(executable, written, StringComparison.Ordinal);
            Assert.Contains("serve --detached", written, StringComparison.Ordinal);
            Assert.StartsWith("@echo off", written, StringComparison.Ordinal);

            Assert.True(autostart.Disable());
            Assert.False(autostart.IsEnabled);
            Assert.False(File.Exists(autostart.EntryPath));

            // A second disable, and a disable on a machine where it was never enabled, remove nothing and
            // say so rather than failing.
            Assert.False(autostart.Disable());
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void A_machine_without_a_startup_folder_refuses_instead_of_writing_somewhere_else()
    {
        var autostart = new AikoAutostart(@"C:\Aiko\bin\aiko.exe", startupDirectory: string.Empty);

        Assert.False(autostart.IsSupported);
        Assert.False(autostart.IsEnabled);
        Assert.False(autostart.Disable());

        // Enabling says what to do instead: silently writing the entry somewhere the machine does not read
        // would leave a person believing the daemon starts at sign-in when it does not.
        var exception = Assert.Throws<InvalidOperationException>(autostart.Enable);
        Assert.Contains("aiko serve --detached", exception.Message, StringComparison.Ordinal);
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
