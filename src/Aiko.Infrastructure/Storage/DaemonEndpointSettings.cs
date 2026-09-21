using Aiko.Infrastructure.Logging;

namespace Aiko.Infrastructure.Storage;

/// <summary>
/// Settings of the Aiko daemon itself: the port, the derived loopback base URL, and how much of its own
/// record it keeps.
/// </summary>
/// <param name="Port">Port the daemon listens on.</param>
/// <param name="Logs">
/// Bounds on the daemon's log file and its event journal, or null when the file states none - which is what
/// every settings file written before the section existed looks like.
/// </param>
public sealed record DaemonEndpointSettings(int Port, LogRetentionSettings? Logs = null)
{
    /// <summary>
    /// Base URL of the daemon on the loopback interface.
    /// </summary>
    public Uri BaseUri => new($"http://127.0.0.1:{Port}");

    /// <summary>
    /// The retention in force: what the file states, or the built-in bounds when it states nothing.
    /// </summary>
    public LogRetentionSettings EffectiveLogs => Logs ?? LogRetentionSettings.Default;
}

