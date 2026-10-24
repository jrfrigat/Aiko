using System.ComponentModel;
using System.Diagnostics;
using Aiko.Application.Installation;
using Aiko.Infrastructure.Installation;
using Aiko.Infrastructure.Storage;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// Waiting for the daemon to be gone, not merely to have stopped answering. The host accepts the stop and
/// then spends time closing the connections it was serving; its files stay in use until the process exits,
/// and an update that trusted the endpoint alone moved them under it and had to be rolled back.
/// </summary>
public sealed class DaemonLifecycleSpecs
{
    [Fact]
    public async Task A_daemon_process_that_does_not_exit_is_refused_before_anything_is_replaced()
    {
        using var fixture = new DaemonFixture();
        fixture.StartDaemonProcess();
        var lifecycle = new DaemonLifecycle(fixture.Paths, TimeSpan.FromMilliseconds(300));

        var refused = await Assert.ThrowsAsync<InstallationRefusedException>(
            () => lifecycle.StopAsync(fixture.InstallDirectory, CancellationToken.None).AsTask());

        // No port was saved, so no stop was sent: the refusal must not claim one, nor send the person to
        // `aiko serve stop`, which knocks on the same missing port.
        Assert.Contains("could not be asked to stop", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("accepted the stop", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("aiko serve stop", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_daemon_that_was_never_asked_to_stop_is_not_waited_on_for_the_whole_stop_timeout()
    {
        using var fixture = new DaemonFixture();
        fixture.StartDaemonProcess();
        // The minute a stopped daemon gets. One that was never asked to stop gets a short grace instead,
        // because nothing this run does will make it leave.
        var lifecycle = new DaemonLifecycle(fixture.Paths, TimeSpan.FromSeconds(60));
        var clock = Stopwatch.StartNew();

        await Assert.ThrowsAsync<InstallationRefusedException>(
            () => lifecycle.StopAsync(fixture.InstallDirectory, CancellationToken.None).AsTask());

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), $"Waited {clock.Elapsed}.");
    }

    [Fact]
    public async Task An_installation_without_a_daemon_process_is_not_waited_on()
    {
        using var fixture = new DaemonFixture();
        var lifecycle = new DaemonLifecycle(fixture.Paths, TimeSpan.FromMilliseconds(300));

        // Nothing answers and nothing runs: the wait is over before it starts rather than spending its
        // timeout on a process that is not there.
        await lifecycle.StopAsync(fixture.InstallDirectory, CancellationToken.None);
    }

    /// <summary>A data directory and an installation whose daemon directory holds a real process.</summary>
    private sealed class DaemonFixture : IDisposable
    {
        private readonly List<Process> processes = [];
        private readonly string executable;

        public DaemonFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "Aiko.Specs", Guid.NewGuid().ToString("N"));
            InstallDirectory = Path.Combine(Root, "bin");
            var serverDirectory = Path.Combine(InstallDirectory, ReleaseLayout.ServerDirectoryName);
            Directory.CreateDirectory(serverDirectory);

            // The image has to be a real one under the daemon's name: the wait reads the process table, and a
            // file would not appear in it.
            executable = Path.Combine(serverDirectory, ReleaseLayout.ServerFileName);
            File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), executable);
            Paths = new AikoDataPaths(Path.Combine(Root, "data", "aiko.db"));
        }

        public string Root { get; }

        public string InstallDirectory { get; }

        public AikoDataPaths Paths { get; }

        /// <summary>Starts a process that outlives any short timeout, from the daemon's own directory.</summary>
        public void StartDaemonProcess()
        {
            var process = Process.Start(new ProcessStartInfo(executable)
            {
                ArgumentList = { "/c", "ping -n 60 127.0.0.1 >nul" },
                UseShellExecute = false,
                CreateNoWindow = true
            }) ?? throw new InvalidOperationException("The fake daemon did not start.");

            processes.Add(process);
        }

        public void Dispose()
        {
            foreach (var process in processes)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }

                    process.WaitForExit(10_000);
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or NotSupportedException or Win32Exception)
                {
                    // A process that already exited, or one this test host may not kill, is not a reason to
                    // fail the spec after its assertions have run.
                }
                finally
                {
                    process.Dispose();
                }
            }

            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }
    }
}
