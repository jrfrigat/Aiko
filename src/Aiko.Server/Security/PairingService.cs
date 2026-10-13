using System.Security.Cryptography;

namespace Aiko.Server.Security;

/// <summary>
/// Issues and consumes one-time pairing codes used to authenticate the local browser.
/// A code is single-use: consuming it removes it from the set of valid codes.
/// </summary>
internal sealed class PairingService
{
    /// <summary>
    /// How long a code stays usable. A person reads it off the console and pastes it, so the window is
    /// minutes rather than seconds - and it closes by itself, instead of waiting for a restart of the daemon
    /// (TASK-159).
    /// </summary>
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(5);

    private readonly object _sync = new();
    private readonly Dictionary<string, DateTimeOffset> _codes = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;

    public PairingService()
        : this(TimeProvider.System)
    {
    }

    /// <summary>Seams the clock, so a spec can move it instead of waiting five minutes.</summary>
    internal PairingService(TimeProvider time) => _time = time;

    /// <summary>
    /// Generates and stores a new one-time pairing code.
    /// </summary>
    public string GenerateCode()
    {
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        lock (_sync)
        {
            var now = _time.GetUtcNow();
            RemoveExpired(now);
            _codes[code] = now + CodeLifetime;
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
            var now = _time.GetUtcNow();
            RemoveExpired(now);
            _codes[code] = now + CodeLifetime;
        }
    }

    /// <summary>
    /// Consumes the code if it is currently valid; returns false otherwise. A code that has expired is not
    /// valid, and is dropped on the way out.
    /// </summary>
    public bool TryConsume(string code)
    {
        lock (_sync)
        {
            var now = _time.GetUtcNow();
            RemoveExpired(now);
            return _codes.Remove(code);
        }
    }

    /// <summary>Drops every code whose window has closed. Called under <see cref="_sync"/>.</summary>
    private void RemoveExpired(DateTimeOffset now)
    {
        if (_codes.Count == 0)
        {
            return;
        }

        foreach (var expired in _codes.Where(entry => entry.Value <= now).Select(entry => entry.Key).ToList())
        {
            _codes.Remove(expired);
        }
    }
}