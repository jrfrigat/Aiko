using System.Security.Cryptography;
using System.Text;
using Aiko.Infrastructure.Storage;

namespace Aiko.Infrastructure.Settings;

/// <summary>
/// Generates and persists the local access token used to authenticate the REST API and MCP.
/// The token is created once and reused across daemon restarts.
/// </summary>
public sealed class AccessTokenStore(AikoDataPaths paths)
{
    /// <summary>
    /// Returns the persisted access token or creates and persists a new one.
    /// </summary>
    public async ValueTask<string> GetOrCreateAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(paths.AccessTokenPath))
        {
            return (await File.ReadAllTextAsync(paths.AccessTokenPath, cancellationToken)).Trim();
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