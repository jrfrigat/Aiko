namespace Aiko.Application.Installation;

/// <summary>
/// The running daemon, as an installation run needs to see it: is it up, stop it, bring it back.
/// </summary>
/// <remarks>
/// The installation engine's second and last seam, and like <see cref="IReleaseSource"/> it is an interface
/// for a reason beyond testing: the engine must not know which file holds the port, what the health endpoint
/// is called or how a process is started, because none of that is its subject - its subject is replacing
/// files, and files cannot be replaced under a process that holds them.
/// <para>
/// A daemon that will not stop is a refusal rather than a fault, and an implementation says so by throwing
/// <see cref="InstallationRefusedException"/>: the run then reports "nothing was changed, and here is why"
/// instead of taking a live installation apart.
/// </para>
/// </remarks>
public interface IDaemonLifecycle
{
    /// <summary>
    /// Whether a daemon for this installation answers right now.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<bool> IsRunningAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stops the daemon and waits until it has really stopped.
    /// </summary>
    /// <remarks>
    /// A no-op when nothing is answering. Waiting matters: the endpoint that accepts the request answers
    /// while the host is still winding down, and replacing the files of a process that has not exited yet is
    /// how a half-replaced installation happens.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InstallationRefusedException">The daemon answers but cannot be stopped.</exception>
    ValueTask StopAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Starts the daemon of the given installation and waits until it answers.
    /// </summary>
    /// <param name="installDirectory">
    /// Directory the binaries were just replaced in: the command that is started is the one that now lives
    /// there, not the one that asked for the run.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether the daemon answered before the wait ran out.</returns>
    ValueTask<bool> StartAsync(string installDirectory, CancellationToken cancellationToken);
}
