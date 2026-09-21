using System.Security.Cryptography;

namespace Aiko.Infrastructure.Installation;

/// <summary>
/// SHA-256 of a file, as lowercase hexadecimal - the spelling a release's <c>SHA256SUMS</c> uses.
/// </summary>
internal static class FileHash
{
    /// <summary>Hashes a file by streaming it, so a large archive is not read into memory.</summary>
    /// <param name="path">File to hash.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }
}
