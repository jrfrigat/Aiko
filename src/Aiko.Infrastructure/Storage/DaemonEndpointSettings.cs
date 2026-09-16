namespace Aiko.Infrastructure.Storage;

/// <summary>
/// Endpoint settings of the Aiko daemon: the port and the derived loopback base URL.
/// </summary>
public sealed record DaemonEndpointSettings(int Port)
{
    /// <summary>
    /// Base URL of the daemon on the loopback interface.
    /// </summary>
    public Uri BaseUri => new($"http://127.0.0.1:{Port}");
}
