using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aiko.Infrastructure.Storage;

/// <summary>
/// Chooses and persists the Aiko daemon port: prefers the fixed port, otherwise
/// finds a free one in the automatic range and atomically saves the choice to settings.json.
/// </summary>
public sealed class DaemonEndpointConfiguration(AikoDataPaths paths)
{
    /// <summary>
    /// Preferred daemon port when it is free.
    /// </summary>
    public const int PreferredPort = 24560;

    /// <summary>
    /// Lower bound of the automatic port range.
    /// </summary>
    public const int FirstAutomaticPort = 18000;

    /// <summary>
    /// Upper bound of the automatic port range.
    /// </summary>
    public const int LastAutomaticPort = 18999;

    /// <summary>
    /// Returns the persisted port settings after checking availability;
    /// when nothing is persisted, chooses and records a new port.
    /// </summary>
    /// <param name="requestedPort">
    /// Port explicitly chosen by the installer; null means use the saved or an automatic one.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    public async ValueTask<DaemonEndpointSettings> LoadOrCreateAsync(
        int? requestedPort,
        CancellationToken cancellationToken)
    {
        var existing = await ReadAsync(cancellationToken);
        if (requestedPort is null && existing is not null)
        {
            ValidatePort(existing.Port);
            EnsureAvailable(existing.Port, persisted: true);
            return existing;
        }

        var port = requestedPort ?? FindAvailablePort();
        ValidatePort(port);
        EnsureAvailable(port, persisted: false);
        var settings = new DaemonEndpointSettings(port);
        await WriteAtomicallyAsync(settings, cancellationToken);
        return settings;
    }

    /// <summary>
    /// Returns the persisted port settings without creating them; null when none are saved yet.
    /// </summary>
    public ValueTask<DaemonEndpointSettings?> TryReadAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(cancellationToken);

    private async ValueTask<DaemonEndpointSettings?> ReadAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(paths.SettingsPath))
        {
            return null;
        }

        await using var input = File.OpenRead(paths.SettingsPath);
        return await JsonSerializer.DeserializeAsync(
                input,
                EndpointJsonContext.Default.DaemonEndpointSettings,
                cancellationToken)
            ?? throw new InvalidDataException(
                $"Invalid Aiko daemon settings: {paths.SettingsPath}");
    }

    private async ValueTask WriteAtomicallyAsync(
        DaemonEndpointSettings settings,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(paths.SettingsPath)
            ?? throw new InvalidOperationException("The settings path must include a directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".settings.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(
                    output,
                    settings,
                    EndpointJsonContext.Default.DaemonEndpointSettings,
                    cancellationToken);
            }

            File.Move(temporaryPath, paths.SettingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static int FindAvailablePort()
    {
        if (IsAvailable(PreferredPort))
        {
            return PreferredPort;
        }

        var offset = RandomNumberGenerator.GetInt32(
            LastAutomaticPort - FirstAutomaticPort + 1);
        for (var index = 0; index <= LastAutomaticPort - FirstAutomaticPort; index++)
        {
            var port =
                FirstAutomaticPort +
                ((offset + index) % (LastAutomaticPort - FirstAutomaticPort + 1));
            if (IsAvailable(port))
            {
                return port;
            }
        }

        throw new IOException(
            $"No free loopback port was found in {FirstAutomaticPort}-{LastAutomaticPort}.");
    }

    private static void ValidatePort(int port)
    {
        if (port is < 1024 or > 65535)
        {
            throw new ArgumentOutOfRangeException(
                nameof(port),
                port,
                "The Aiko daemon port must be between 1024 and 65535.");
        }
    }

    private static void EnsureAvailable(int port, bool persisted)
    {
        if (IsAvailable(port))
        {
            return;
        }

        var source = persisted ? "saved" : "requested";
        throw new IOException(
            $"The {source} Aiko port {port} is already in use. " +
            "Run the Aiko installer to choose and apply another port.");
    }

    private static bool IsAvailable(int port)
    {
        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        finally
        {
            listener?.Stop();
        }
    }
}
