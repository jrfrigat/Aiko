using System.Net;
using System.Net.Sockets;
using Aiko.Infrastructure.Storage;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// What the daemon says when its saved port is taken. The message is the only guidance a person gets at that
/// moment, so it has to name a step that exists: there is no installer option for the port, and what works is
/// starting the daemon once with AIKO_PORT and then letting repair rewrite the agents' configurations.
/// </summary>
public sealed class PortConflictSpecs
{
    [Fact]
    public async Task A_busy_saved_port_names_the_steps_that_move_the_daemon_to_another_port()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Aiko.PortConflict", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var configuration = new DaemonEndpointConfiguration(
            new AikoDataPaths(Path.Combine(directory, "aiko.db")));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            await configuration.SaveAsync(new DaemonEndpointSettings(port), CancellationToken.None);

            var refused = await Assert.ThrowsAsync<IOException>(async () =>
                await configuration.LoadOrCreateAsync(null, CancellationToken.None));

            Assert.Contains(port.ToString(System.Globalization.CultureInfo.InvariantCulture), refused.Message, StringComparison.Ordinal);
            Assert.Contains("AIKO_PORT", refused.Message, StringComparison.Ordinal);
            Assert.Contains("aiko repair --fix", refused.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("installer", refused.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            listener.Stop();
            Directory.Delete(directory, recursive: true);
        }
    }
}
