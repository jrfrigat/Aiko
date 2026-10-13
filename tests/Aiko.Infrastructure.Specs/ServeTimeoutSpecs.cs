using System.Diagnostics;
using System.Globalization;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// `aiko serve -d` that gives up on the daemon it started must end it, not merely stop waiting for it.
/// </summary>
/// <remarks>
/// Disposing a process releases the handle and leaves it running, which is how a daemon that never answered
/// survived the command that started it: the next `aiko serve` found its port taken by a process nobody
/// could name, and `aiko serve stop` could not reach a daemon that never recorded its port (TASK-343).
/// <para>
/// The daemon here is a substitute, because there is no other way to reach this path: a real one either
/// answers or ends by itself - a busy port makes <c>LoadOrCreateAsync</c> throw and the daemon return 1 -
/// so with a real daemon "it is gone" would pass without anything having been ended.
/// <see cref="CliLayout"/> lays out a copy of the built CLI with that substitute beside it, named
/// <c>Aiko.Server.dll</c>, which is exactly what `ResolveServerCommand` looks for.
/// </para>
/// </remarks>
public sealed class ServeTimeoutSpecs
{
    [Fact]
    public async Task A_daemon_that_never_answers_is_ended_rather_than_left_running()
    {
        using var layout = new CliLayout();

        var (exitCode, output) = await layout.RunServeDetachedAsync();

        // The command says what happened and fails: a "started" that nothing can connect to is not an answer.
        Assert.Equal(1, exitCode);
        Assert.Contains("did not answer on its port within 1 seconds", output, StringComparison.Ordinal);
        Assert.Contains("It has been stopped", output, StringComparison.Ordinal);

        // The daemon really was started - the substitute writes its own id before it starts waiting - so that
        // the check below is about a process that existed rather than about one that never ran at all.
        Assert.True(
            File.Exists(layout.DaemonPidFile),
            $"The daemon never recorded its id in {layout.DaemonPidFile}.{Environment.NewLine}{output}");
        var daemon = int.Parse(File.ReadAllText(layout.DaemonPidFile), CultureInfo.InvariantCulture);

        // And it is gone. The substitute waits without an end, so nothing but the kill could have ended it.
        Assert.True(
            await EndsAsync(daemon),
            $"The daemon (pid {daemon}) was still running after `aiko serve -d` gave up on it.");
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

    /// <summary>
    /// A copy of the built CLI with a daemon that never answers laid out beside it, and a data directory of
    /// its own. Everything lives under one temporary root that dispose removes.
    /// </summary>
    private sealed class CliLayout : IDisposable
    {
        private static readonly string Configuration =
            new DirectoryInfo(Path.GetDirectoryName(typeof(ServeTimeoutSpecs).Assembly.Location)!).Parent!.Name;

        private readonly string _logPath;

        public CliLayout()
        {
            Root = Path.Combine(Path.GetTempPath(), "Aiko.ServeTimeout", Guid.NewGuid().ToString("N"));
            Layout = Path.Combine(Root, "layout");
            DatabasePath = Path.Combine(Root, "data", "aiko.db");
            DaemonPidFile = Path.Combine(Root, "daemon.pid");
            _logPath = Path.Combine(Root, "daemon.log");
            Directory.CreateDirectory(Layout);
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);

            Copy(CliOutputDirectory(), Layout);
            LayOutSubstitute();
        }

        public string Root { get; }

        /// <summary>What <c>AppContext.BaseDirectory</c> of the CLI under test is, and where a daemon is looked for.</summary>
        public string Layout { get; }

        public string DatabasePath { get; }

        /// <summary>Where the substitute writes its own id, so that "it ran" is a fact and not an assumption.</summary>
        public string DaemonPidFile { get; }

        public async Task<(int ExitCode, string Output)> RunServeDetachedAsync()
        {
            var startInfo = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(Path.Combine(Layout, "aiko.dll"));
            startInfo.ArgumentList.Add("serve");
            startInfo.ArgumentList.Add("-d");

            // Only this spec's data directory: an Aiko installed on the machine must not be touched or read,
            // and an AIKO_PORT from the environment would send the health check somewhere else entirely.
            var inherited = startInfo.Environment.Keys
                .Where(key => key.StartsWith("AIKO_", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            foreach (var name in inherited)
            {
                startInfo.Environment.Remove(name);
            }

            startInfo.Environment["AIKO_DATABASE"] = DatabasePath;
            startInfo.Environment["AIKO_USER_HOME"] = Path.Combine(Root, "home");
            startInfo.Environment["AIKO_LOG_FILE"] = _logPath;
            startInfo.Environment["AIKO_SILENT_DAEMON_PID_FILE"] = DaemonPidFile;
            // Thirty seconds of waiting to see a timeout is thirty seconds this suite does not have, and the
            // knob exists for exactly this (TASK-343).
            startInfo.Environment["AIKO_SERVE_WAIT_SECONDS"] = "1";

            using var process = Process.Start(startInfo)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(1));
            }
            catch (TimeoutException exception)
            {
                // `using` only releases the handle: a run that overran must not leave the CLI - and the daemon
                // it started - behind for the rest of the session.
                process.Kill(entireProcessTree: true);
                throw new TimeoutException(
                    $"`aiko serve -d` did not exit within a minute."
                    + $"{Environment.NewLine}{await output}{await error}",
                    exception);
            }

            return (process.ExitCode, await ReadAsync(output, error));
        }

        /// <summary>
        /// What the command said, waiting for it only so long. The daemon it starts inherits these pipes, so
        /// they close when nothing holds them any more - which is the very thing this spec is about: a
        /// regression that leaves the daemon running holds them open, and this must fail rather than hang.
        /// </summary>
        private static async Task<string> ReadAsync(Task<string> standardOutput, Task<string> standardError)
        {
            try
            {
                await Task.WhenAll(standardOutput, standardError).WaitAsync(TimeSpan.FromSeconds(15));
            }
            catch (TimeoutException)
            {
                return "(the command's output is still being held open: something it started is still running)";
            }

            return standardOutput.Result + standardError.Result;
        }

        /// <summary>The CLI built in the same configuration as this spec assembly.</summary>
        private static string CliOutputDirectory()
        {
            var directory = Path.Combine(
                CliInitSpecs.FindRepositoryRoot(), "src", "Aiko.Cli", "bin", Configuration, "net10.0");
            return File.Exists(Path.Combine(directory, "aiko.dll"))
                ? directory
                : throw new FileNotFoundException("Build Aiko.Cli first.", directory);
        }

        /// <summary>
        /// Puts the substitute where `ResolveServerCommand` looks for a daemon, under the name it looks for.
        /// Its runtime configuration goes with the assembly: without one, `dotnet` cannot run it at all.
        /// </summary>
        private void LayOutSubstitute()
        {
            var built = Path.Combine(
                CliInitSpecs.FindRepositoryRoot(),
                "tests", "Aiko.Infrastructure.Specs", "Fixtures", "SilentDaemon", "bin", Configuration);
            var assembly = Directory
                .EnumerateFiles(built, "SilentDaemon.dll", SearchOption.AllDirectories)
                .FirstOrDefault()
                ?? throw new FileNotFoundException(
                    "Build Aiko.Infrastructure.Specs first: the substitute daemon is built with it.", built);
            var directory = Path.GetDirectoryName(assembly)!;

            File.Copy(assembly, Path.Combine(Layout, "Aiko.Server.dll"), overwrite: true);
            File.Copy(
                Path.Combine(directory, "SilentDaemon.runtimeconfig.json"),
                Path.Combine(Layout, "Aiko.Server.runtimeconfig.json"),
                overwrite: true);
            File.Copy(
                Path.Combine(directory, "SilentDaemon.deps.json"),
                Path.Combine(Layout, "Aiko.Server.deps.json"),
                overwrite: true);

            // A real daemon beside the CLI would be started instead of this one - and would answer.
            Assert.False(File.Exists(Path.Combine(Layout, "Aiko.Server.exe")));
        }

        /// <summary>Every file of a built directory, so that the copy is a layout the CLI can run in.</summary>
        private static void Copy(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.EnumerateFiles(source))
            {
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
            }

            foreach (var directory in Directory.EnumerateDirectories(source))
            {
                Copy(directory, Path.Combine(destination, Path.GetFileName(directory)));
            }
        }

        public void Dispose()
        {
            // A regression here is the very thing this spec is about, so a run that fails leaves the substitute
            // behind - and failing is not a reason to keep a process alive on the machine that ran the suite.
            if (File.Exists(DaemonPidFile) &&
                int.TryParse(
                    File.ReadAllText(DaemonPidFile),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var pid))
            {
                try
                {
                    using var daemon = Process.GetProcessById(pid);
                    daemon.Kill(entireProcessTree: true);
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
                {
                }
            }

            TempRoot.Delete(Root);
        }
    }
}
