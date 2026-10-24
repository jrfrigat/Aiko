using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Aiko.Application.Installation;
using Aiko.Infrastructure.Settings;
using Aiko.Infrastructure.Storage;

namespace Aiko.Infrastructure.Installation;

/// <summary>
/// The real daemon, reached the way every other command reaches it: the port it saved, its health endpoint
/// and its own shutdown endpoint.
/// </summary>
/// <remarks>
/// The stop goes through the daemon's own endpoint rather than the process table, exactly as
/// <c>aiko serve stop</c> does, and for the same reason: the daemon owns its shutdown, so it can finish what
/// it is doing instead of being killed mid-run. Two things follow from that and are handled here - the
/// endpoint answers while the host is still winding down, so the port is polled until it really stops, and a
/// daemon that keeps answering after accepting the stop is a refusal, because the files cannot be replaced
/// under it. The process is waited for as well: the host stops listening before it has finished closing what
/// it was serving, and its files stay in use until it has exited.
/// <para>
/// Nothing here writes the settings: the port is read and left alone, which is the promise that an update
/// does not move a running installation's port.
/// </para>
/// </remarks>
/// <param name="paths">The daemon's data directory: where its saved port and access token live.</param>
/// <param name="stopTimeout">
/// How long the process gets to exit after accepting the stop. A parameter because a spec cannot spend the
/// real minute proving a refusal.
/// </param>
public sealed class DaemonLifecycle(AikoDataPaths paths, TimeSpan? stopTimeout = null) : IDaemonLifecycle
{
    private const string HealthPath = "/health";
    private const string ShutdownPath = "/api/v1/system/shutdown";

    /// <summary>How long a single request may take before the daemon counts as not answering.</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    /// <summary>How long a start waits for the daemon to answer before it is reported as not up.</summary>
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long the process itself gets to exit once it has accepted the stop. The host closes the
    /// connections it was serving before it exits, which is bounded by its own shutdown timeout of 30
    /// seconds, so a minute is the polite version of "gone".
    /// </summary>
    private readonly TimeSpan exitTimeout = stopTimeout ?? TimeSpan.FromSeconds(60);

    /// <summary>How often the process table is asked whether the daemon is gone.</summary>
    private static readonly TimeSpan ExitPoll = TimeSpan.FromMilliseconds(250);

    /// <inheritdoc />
    public async ValueTask<bool> IsRunningAsync(CancellationToken cancellationToken)
    {
        var port = await SavedPortAsync(cancellationToken);
        return port is not null && await IsHealthyAsync(port.Value, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask StopAsync(string installDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);

        var port = await SavedPortAsync(cancellationToken);
        if (port is not null && await IsHealthyAsync(port.Value, cancellationToken))
        {
            using var http = CreateClient(port.Value);
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", await AccessTokenAsync(cancellationToken));

            try
            {
                using var response = await http.PostAsync(ShutdownPath, content: null, cancellationToken);
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    throw new InstallationRefusedException(
                        $"The daemon on port {port} refused the access token, so it cannot be stopped before its " +
                        "files are replaced. If it was started with AIKO_TOKEN, run this with the same value set.");
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new InstallationRefusedException(
                        $"The daemon on port {port} answered {(int)response.StatusCode} to the shutdown request, " +
                        "so nothing was replaced.");
                }
            }
            catch (HttpRequestException)
            {
                // It went away between the health check and the request: that is stopped, which is all the
                // endpoint side of this method promises.
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new InstallationRefusedException(
                    $"The daemon on port {port} did not answer within {RequestTimeout.TotalSeconds:0} seconds, " +
                    "so nothing was replaced.");
            }

            var answered = true;
            for (var attempt = 0; attempt < 20 && answered; attempt++)
            {
                await Task.Delay(250, cancellationToken);
                answered = await IsHealthyAsync(port.Value, cancellationToken);
            }

            if (answered)
            {
                throw new InstallationRefusedException(
                    $"The daemon on port {port} accepted the stop but is still answering, so it may be " +
                    "finishing a run. Nothing was replaced; stop it with `aiko serve stop` and run this again.");
            }
        }

        // The endpoint and the process are two different states: the host stops listening while it is still
        // closing the connections it was serving, and its files stay in use until it has exited. An update
        // that trusted only the endpoint moved the binaries under a live process and had to be rolled back.
        await WaitForExitAsync(installDirectory, cancellationToken);
    }

    /// <summary>
    /// Waits until no daemon process runs out of the installation, and refuses when one does not leave.
    /// </summary>
    /// <remarks>
    /// A refusal here happens before anything is moved: the installation is still whole, and running the
    /// command again after the daemon has gone is the whole recovery.
    /// </remarks>
    private async ValueTask WaitForExitAsync(string installDirectory, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + exitTimeout;
        while (DaemonIsRunning(installDirectory))
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new InstallationRefusedException(
                    $"The daemon accepted the stop but is still running after {exitTimeout.TotalSeconds:0} " +
                    "seconds, so its files are still in use. Nothing was replaced; run `aiko serve stop` and " +
                    "try again.");
            }

            await Task.Delay(ExitPoll, cancellationToken);
        }
    }

    /// <summary>
    /// Whether a daemon process from this installation is still alive.
    /// </summary>
    /// <remarks>
    /// By the daemon's own name and the directory its binary lives in: the agents' proxies run from the
    /// installation too and are not the daemon, and another installation's daemon is not this one.
    /// </remarks>
    private static bool DaemonIsRunning(string installDirectory)
    {
        var serverDirectory = Path.Combine(Path.GetFullPath(installDirectory), ReleaseLayout.ServerDirectoryName);
        var prefix = Path.TrimEndingDirectorySeparator(serverDirectory) + Path.DirectorySeparatorChar;

        foreach (var process in Process.GetProcessesByName(
            Path.GetFileNameWithoutExtension(ReleaseLayout.ServerFileName)))
        {
            using (process)
            {
                try
                {
                    if (process.MainModule?.FileName is { } image &&
                        Path.GetFullPath(image).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch (Exception exception) when (
                    exception is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // A process that has exited between the listing and the question, or one the system will
                    // not let this run question, is not evidence of a daemon holding this installation.
                }
            }
        }

        return false;
    }

    /// <inheritdoc />
    public async ValueTask<bool> StartAsync(string installDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);

        // The port is what a start is waited on, so an installation that never had one has nothing to wait
        // for: the daemon chooses and saves it on its first run, and that run is the person's to make.
        var port = await SavedPortAsync(cancellationToken);
        if (port is null)
        {
            return false;
        }

        if (await IsHealthyAsync(port.Value, cancellationToken))
        {
            return true;
        }

        // The binary that is started is the one that has just been put in place - starting the one that
        // asked for the update would run the old version and defeat the point of the restart.
        var executable = Path.Combine(installDirectory, ReleaseLayout.CliFileName);
        if (!File.Exists(executable))
        {
            return false;
        }

        using var process = Process.Start(new ProcessStartInfo(executable, "serve --detached")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        });
        if (process is null)
        {
            return false;
        }

        var deadline = DateTime.UtcNow + StartupTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await IsHealthyAsync(port.Value, cancellationToken))
            {
                return true;
            }

            await Task.Delay(250, cancellationToken);
        }

        return false;
    }

    /// <summary>
    /// The access token the daemon was started with.
    /// </summary>
    /// <remarks>
    /// <c>AIKO_TOKEN</c> wins over the stored one, as in <c>aiko serve stop</c>: a daemon started with that
    /// variable set would otherwise answer 401 to the very command trying to stop it.
    /// </remarks>
    private async ValueTask<string> AccessTokenAsync(CancellationToken cancellationToken)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("AIKO_TOKEN");
        return string.IsNullOrWhiteSpace(fromEnvironment)
            ? await new AccessTokenStore(paths).GetOrCreateAsync(cancellationToken)
            : fromEnvironment;
    }

    /// <summary>The port the daemon saved, or null when it has never run here.</summary>
    private async ValueTask<int?> SavedPortAsync(CancellationToken cancellationToken) =>
        (await new DaemonEndpointConfiguration(paths).TryReadAsync(cancellationToken))?.Port;

    /// <summary>
    /// Whether the daemon answers its health endpoint. A refusal to answer is the answer "it is not running",
    /// which is why this returns a bool rather than throwing.
    /// </summary>
    private static async ValueTask<bool> IsHealthyAsync(int port, CancellationToken cancellationToken)
    {
        using var http = CreateClient(port);
        try
        {
            using var response = await http.GetAsync(HealthPath, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    private static HttpClient CreateClient(int port) =>
        new()
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
            Timeout = RequestTimeout
        };
}
