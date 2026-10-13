using System.Diagnostics;
using System.Globalization;
using Aiko.Application.Contracts;
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
/// </remarks>
public sealed class GitClientRefusalSpecs
{
    [Fact]
    public async Task A_command_that_never_answers_is_refused_and_ended()
    {
        var root = Path.Combine(Path.GetTempPath(), "Aiko.GitRefusal", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var pidFile = Path.Combine(root, "git.pid");
        try
        {
            // A "git" of our own: it writes down which process it is and then waits. Everything the client
            // appends after the command - `-C <dir>`, then the question - lands after the comment marker, so
            // the substitute is never asked a question it would have to answer (TASK-349).
            var client = new GitClient
            {
                Command = ("powershell", [
                    "-NoProfile",
                    "-Command",
                    $"Set-Content -LiteralPath '{pidFile}' -Value $PID; Start-Sleep -Seconds 120; #"
                ]),
                // Waiting fifteen seconds for a refusal is fifteen seconds this suite does not have, and the
                // wait is a seam of its own so that a spec can reach the branch without inventing anything.
                CommandWait = TimeSpan.FromSeconds(5)
            };

            var status = await client.StatusAsync(root, CancellationToken.None);

            // The caller is told, not thrown at: the same answer a machine without git gets.
            Assert.False(status.Available, "A command that never answered should be reported as unavailable.");
            Assert.Equal(GitClient.ClientUnavailable, status.Failure);
            Assert.False(status.IsRepository);

            // The substitute really ran - otherwise "it is gone" would say nothing about the branch - and it
            // is gone, which is what TASK-160 promised and this card checks.
            Assert.True(
                File.Exists(pidFile),
                $"The substituted command never recorded its process id in {pidFile}.");
            var pid = int.Parse(File.ReadAllText(pidFile).Trim(), CultureInfo.InvariantCulture);
            Assert.True(
                await EndsAsync(pid),
                $"The command (pid {pid}) was still running after the client gave up on it.");
        }
        finally
        {
            TempRoot.Delete(root);
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
