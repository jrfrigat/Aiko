using System.Security.Cryptography;
using System.Text;
using Aiko.Infrastructure.Storage;

namespace Aiko.Infrastructure.Settings;

/// <summary>
/// Generates and persists the local access token the REST API and MCP authenticate with: created once and
/// reused across daemon restarts.
/// </summary>
public sealed class AccessTokenStore(AikoDataPaths paths)
{
    /// <summary>
    /// The shortest token the daemon accepts. The generated one is longer; the floor exists because a token is
    /// compared for equality, so a short - or empty - one is not a weaker secret but no secret at all.
    /// </summary>
    public const int MinimumLength = 32;

    /// <summary>
    /// Returns the persisted access token or creates and persists a new one; a stored token shorter than
    /// <see cref="MinimumLength"/> is replaced rather than trusted.
    /// </summary>
    public async ValueTask<string> GetOrCreateAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(paths.AccessTokenPath))
        {
            var stored = (await File.ReadAllTextAsync(paths.AccessTokenPath, cancellationToken)).Trim();
            if (stored.Length >= MinimumLength)
            {
                return stored;
            }
        }

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await WriteAtomicallyAsync(token, cancellationToken);
        return token;
    }

    private async ValueTask WriteAtomicallyAsync(string token, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(paths.AccessTokenPath)
            ?? throw new InvalidOperationException("The access token path must include a directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{paths.AccessTokenPath}.{Guid.NewGuid():N}.tmp";
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
                var bytes = Encoding.UTF8.GetBytes(token);
                await output.WriteAsync(bytes, cancellationToken);
            }

            File.Move(temporaryPath, paths.AccessTokenPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}