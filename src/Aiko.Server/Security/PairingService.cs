using System.Security.Cryptography;

namespace Aiko.Server.Security;

/// <summary>
/// Issues and consumes one-time pairing codes used to authenticate the local browser.
/// A code is single-use: consuming it removes it from the set of valid codes.
/// </summary>
internal sealed class PairingService
{
    private readonly object _sync = new();
    private readonly HashSet<string> _codes = new(StringComparer.Ordinal);

    /// <summary>
    /// Generates and stores a new one-time pairing code.
    /// </summary>
    public string GenerateCode()
    {
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        lock (_sync)
        {
            _codes.Add(code);
        }

        return code;
    }

    /// <summary>
    /// Seeds a known code (used by tests through the AIKO_PAIR_CODE environment variable).
    /// </summary>
    public void Seed(string code)
    {
        lock (_sync)
        {
            _codes.Add(code);
        }
    }

    /// <summary>
    /// Consumes the code if it is currently valid; returns false otherwise.
    /// </summary>
    public bool TryConsume(string code)
    {
        lock (_sync)
        {
            return _codes.Remove(code);
        }
    }
}