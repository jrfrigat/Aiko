using System.Diagnostics;
using Aiko.Infrastructure.Git;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// A git command this client stops waiting for must not be left running. Disposing the process releases the
/// handle and leaves the command alive - which is how a git that never answers survived a timeout, and how
/// every timeout after it added another one to the machine (TASK-160).
/// </summary>
public sealed class GitClientEndProcessSpecs
{
    [Fact]
    public void A_process_that_is_no_longer_waited_for_is_ended()
    {
        using var sleeper = StartSleeper();
        Assert.False(sleeper.HasExited, "The sleeper should be running when the client gives up on it.");

        GitClient.EndProcess(sleeper);

        // Ended, and not merely disposed: the difference is the whole point of the card.
        Assert.True(sleeper.WaitForExit(10_000), "The process was left running after the client gave up on it.");
    }

    [Fact]
    public void A_process_that_already_ended_is_not_an_error()
    {
        using var finished = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd",
            ArgumentList = { "/c", "exit 0" },
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("The short-lived command did not start.");
        Assert.True(finished.WaitForExit(10_000));

        // A command can end between the check and the kill, and nothing was started at all when
        // Process.Start returns null: neither is a reason to throw.
        GitClient.EndProcess(finished);
        GitClient.EndProcess(null);
    }

    /// <summary>A command that outlives any timeout, so the client has something to give up on.</summary>
    private static Process StartSleeper() =>
        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell",
            ArgumentList = { "-NoProfile", "-Command", "Start-Sleep -Seconds 60" },
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("The sleeper did not start.");
}
