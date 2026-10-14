namespace Aiko.Application.Installation;

/// <summary>
/// The running daemon, as an installation run needs to see it: is it up, stop it, bring it back.
/// </summary>
/// <remarks>
/// The engine's subject is replacing files, and files cannot be replaced under a process that holds them - so a
/// seam it is, even though nothing here is worth faking: the engine must not have to know which file holds the
/// port, what the health endpoint is called or how a process is started.
/// <para>
/// A daemon that will not stop is a refusal rather than a fault, said by throwing
/// <see cref="InstallationRefusedException"/>: the run then reports "nothing was changed, and here is why"
/// instead of taking a live installation apart.
/// </para>
/// </remarks>
public interface IDaemonLifecycle
{
    /// <summary>Whether a daemon for this installation answers right now.</summary>
    ValueTask<bool> IsRunningAsync(CancellationToken cancellationToken);

    /// <summary>Stops the daemon and waits until it has really stopped.</summary>
    /// <remarks>
    /// A no-op when nothing is answering. Waiting is the point: the endpoint answers while the host is still
    /// winding down, and replacing the files of a process that has not exited yet half-replaces an installation.
    /// </remarks>
    /// <exception cref="InstallationRefusedException">The daemon answers but cannot be stopped.</exception>
    ValueTask StopAsync(CancellationToken cancellationToken);

    /// <summary>Starts the daemon of the given installation and waits until it answers.</summary>
    /// <remarks>
    /// The command started is the one that now lives in <paramref name="installDirectory"/> - the binaries were
    /// just replaced there - not the one that asked for the run.
    /// </remarks>
    /// <returns>Whether the daemon answered before the wait ran out.</returns>
    ValueTask<bool> StartAsync(string installDirectory, CancellationToken cancellationToken);
}
