using System.Diagnostics;
using System.Globalization;
using Aiko.Infrastructure.Git;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// A command that never answers must be refused and left nowhere on the machine.
/// </summary>
/// <remarks>
/// The branch itself was TASK-160: a git that hangs is given up on, and the process is ended rather than only
/// released, because a handle released is a command still running and every timeout would add another one.
/// The branch was covered only where it could be reached without a hanging git - <c>GitClientEndProcessSpecs</c>
/// holds <c>EndProcess</c> itself - while the run that reaches it had no spec at all, because a real git
/// cannot be made to hang on demand (TASK-349).
/// <para>
/// So the client is asked to run a command of the spec's own, through the one seam it offers for that
/// (<see cref="GitClient.Command"/>), and asked to wait a short while rather than a quarter of a minute
/// (<see cref="GitClient.CommandWait"/>). The substitute records its own process id and then waits; a
/// substituted command that never starts would fail the id check below rather than pass quietly.
/// </para>
/// <para>
/// One window used to carry two promises at once: the substitute had to finish starting and record itself,
/// <em>and</em> the client's patience had to run out. On a fresh, loaded runner the first could take as long as
/// the second, and the spec failed with "the substitute never recorded its process id" - a red that says
/// nothing about the client, which is what CI did on a commit whose release run was green (TASK-354). The
/// substitute's half of the window is settled before the window opens now: the same command is run once with a
/// one-second wait, which proves the recording works here and warms <c>powershell.exe</c> and its modules. What
/// the window still measures is the client's patience, and nothing else.
/// </para>
/// </remarks>
public sealed class GitClientRefusalSpecs
{
    /// <summary>
    /// How long a question may take before it counts as unanswered. Five seconds rather than the product's
    /// fifteen, so the refusal is reached without waiting a quarter of a minute; five rather than one, because
    /// the window is now margin for a runner that is slow in general rather than a bet on the substitute's cold
    /// start - which the proof below settles before the window opens (TASK-353, TASK-354).
    /// </summary>
    private static readonly TimeSpan ClientWait = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task A_command_that_never_answers_is_refused_and_ended()
    {
        var root = Path.Combine(Path.GetTempPath(), "Aiko.GitRefusal", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var pidFile = Path.Combine(root, "git.pid");
        try
        {
            // Settled before the clock starts: this machine starts the substituted command, and the command
            // writes down which process it is. A failure here is about the machine, not about the client, and
            // the message says so rather than leaving the reader to guess (TASK-354).
            var proof = await RecordsItselfAsync(root, Path.Combine(root, "proof.pid"));
            Assert.True(
                proof is null,
                $"The substituted command does not record itself on this machine, so nothing below could be about the client: {proof}");

            // A "git" of our own: it writes down which process it is and then waits without an end, so that
            // nothing but the kill could have ended it. Everything the client appends after the command -
            // `-C <dir>`, then the question - lands after the comment marker, so the substitute is never asked a
            // question it would have to answer (TASK-349).
            var client = new GitClient
            {
                Command = ("powershell", [
                    "-NoProfile",
                    "-Command",
                    $"Set-Content -LiteralPath '{pidFile}' -Value $PID; "
                    + "[System.Threading.Thread]::Sleep([System.Threading.Timeout]::Infinite); #"
                ]),
                CommandWait = ClientWait
            };

            var status = await client.StatusAsync(root, CancellationToken.None);

            // The caller is told, not thrown at: the same answer a machine without git gets.
            Assert.False(status.Available, "A command that never answered should be reported as unavailable.");
            Assert.Equal(GitClient.ClientUnavailable, status.Failure);
            Assert.False(status.IsRepository);

            // The substitute really ran - the proof above is about this machine in general, this is the process
            // of this run - and it is gone, which is what TASK-160 promised and this card checks.
            Assert.True(
                File.Exists(pidFile),
                $"The substituted command recorded itself in the proof and not within the client's "
                + $"{ClientWait.TotalSeconds:N0} s window: {pidFile}. Nothing here is about the client.");
            var pid = int.Parse(File.ReadAllText(pidFile).Trim(), CultureInfo.InvariantCulture);
            Assert.True(
                await EndsAsync(pid),
                $"The command (pid {pid}) was still running after the client gave up on it.");
        }
        finally
        {
            EndSubstitute(pidFile);
            TempRoot.Delete(root);
        }
    }

    /// <summary>
    /// Runs the substituted command once, with a one-second wait instead of an endless one, and answers
    /// <c>null</c> when it recorded its process id - or the reason it did not.
    /// </summary>
    /// <remarks>
    /// Two things at once, and both are needed before the window opens: it proves that this machine starts the
    /// substitute and that the substitute writes its id, and it warms <c>powershell.exe</c> and its modules,
    /// whose cold start is the whole of TASK-354.
    /// </remarks>
    private static async Task<string?> RecordsItselfAsync(string root, string pidFile)
    {
        var startInfo = new ProcessStartInfo("powershell")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add($"Set-Content -LiteralPath '{pidFile}' -Value $PID; Start-Sleep -Seconds 1");

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return "powershell could not be started at all.";
        }

        var error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(1));
        }
        catch (TimeoutException)
        {
            GitClient.EndProcess(process);
            return "the substituted command did not end within a minute.";
        }

        return File.Exists(pidFile)
            ? null
            : $"it ended with {process.ExitCode} and never wrote {pidFile}: {await error}";
    }

    /// <summary>
    /// Ends the substitute of this run if it is still there.
    /// </summary>
    /// <remarks>
    /// Its wait has no end, so a run that fails before the client kills it would leave a process behind for
    /// good. The same reason <c>ServeTimeoutSpecs</c> disposes the daemon it started: a failing run is not a
    /// reason to keep something alive on the machine that ran the suite.
    /// </remarks>
    private static void EndSubstitute(string pidFile)
    {
        if (!File.Exists(pidFile) ||
            !int.TryParse(File.ReadAllText(pidFile).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid))
        {
            return;
        }

        try
        {
            using var substitute = Process.GetProcessById(pid);
            substitute.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or NotSupportedException or
            System.ComponentModel.Win32Exception)
        {
            // Ended by itself or by the client between the check and the kill: nothing is left to end.
        }
    }

    /// <summary>
    /// Whether nothing carries that process id any more. Ending a process and observing it are two moments,
    /// so this waits a little rather than reading the table once.
    /// </summary>
    private static async Task<bool> EndsAsync(int pid)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                if (process.HasExited)
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                // No process carries that id: it is gone, which is the answer this waits for.
                return true;
            }

            await Task.Delay(100);
        }

        return false;
    }
}
