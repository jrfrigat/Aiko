using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// What <c>scripts/install.ps1</c> - the documented way to install and to update - may do to the directory it
/// is given. It is PowerShell run through <c>irm | iex</c>, so the rules are read from its text, the way the
/// release workflow is: a directory that is not Aiko's is never emptied, a running daemon is stopped before its
/// files are replaced, and a failed copy puts the previous installation back instead of leaving half of one.
/// </summary>
public sealed class InstallScriptSpecs
{
    [Fact]
    public void The_script_refuses_a_directory_that_holds_something_other_than_aiko()
    {
        var script = Script();

        Assert.Contains("function Assert-InstallDirIsOurs", script, StringComparison.Ordinal);
        Assert.Contains("holds no Aiko installation", script, StringComparison.Ordinal);
        // The check runs before the download: refusing costs nothing yet.
        Assert.True(
            script.IndexOf("Assert-InstallDirIsOurs $InstallDir", StringComparison.Ordinal) <
            script.IndexOf("Invoke-WebRequest", StringComparison.Ordinal),
            "the directory check should run before the download");
    }

    [Fact]
    public void The_script_stops_the_daemon_and_swaps_with_a_way_back()
    {
        var script = Script();

        // The old way emptied the directory entry by entry and stopped at the first locked file. What is left is
        // the rollback's clean-up of a failed copy, which must not stop half-way either.
        Assert.DoesNotContain(
            "Get-ChildItem -Path $InstallDir -Force | Remove-Item -Recurse -Force\n",
            script.Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparison.Ordinal);
        Assert.Contains("Move-Item -Destination $previous", script, StringComparison.Ordinal);
        Assert.Contains("serve stop", script, StringComparison.Ordinal);
        Assert.Contains(".previous-", script, StringComparison.Ordinal);
        Assert.Contains("the previous installation was put back", script, StringComparison.Ordinal);
    }

    private static string Script() =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "install.ps1"));

    /// <summary>Walks up from this assembly to the solution file, as the other specs do.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(InstallScriptSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
