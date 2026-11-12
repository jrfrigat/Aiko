using System.Collections.Concurrent;

namespace Aiko.Server.Security;

/// <summary>
/// Short-lived tokens issued to one ACP session. A token opens the MCP endpoint of its own project and
/// nothing else, and the daemon forgets it when the session ends.
/// </summary>
internal sealed class ScopedAccessTokenStore(TimeProvider? time = null)
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, Scope> tokens = new(StringComparer.Ordinal);

    /// <summary>Issues a token for one project handle and delivery.</summary>
    public string Issue(string projectHandle, string deliveryId, TimeSpan lifetime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectHandle);
        ArgumentException.ThrowIfNullOrWhiteSpace(deliveryId);
        var token = $"aiko-scoped-{Guid.NewGuid():N}";
        tokens[token] = new Scope(
            projectHandle.Trim(),
            deliveryId.Trim(),
            clock.GetUtcNow() + lifetime);
        Sweep();
        return token;
    }

    /// <summary>True when the token belongs to that project and has not expired.</summary>
    public bool IsValid(string token, string? projectHandle)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(projectHandle))
        {
            return false;
        }

        if (!tokens.TryGetValue(token, out var scope))
        {
            return false;
        }

        if (scope.ExpiresAtUtc <= clock.GetUtcNow())
        {
            tokens.TryRemove(token, out _);
            return false;
        }

        return StringComparer.OrdinalIgnoreCase.Equals(
            scope.ProjectHandle,
            projectHandle.Trim());
    }

    /// <summary>Forgets one token.</summary>
    public void Revoke(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        tokens.TryRemove(token, out _);
    }

    /// <summary>Forgets every token issued for one delivery.</summary>
    public void RevokeDelivery(string deliveryId)
    {
        foreach (var entry in tokens)
        {
            if (StringComparer.Ordinal.Equals(entry.Value.DeliveryId, deliveryId))
            {
                tokens.TryRemove(entry.Key, out _);
            }
        }
    }

    private void Sweep()
    {
        var now = clock.GetUtcNow();
        foreach (var entry in tokens)
        {
            if (entry.Value.ExpiresAtUtc <= now)
            {
                tokens.TryRemove(entry.Key, out _);
            }
        }
    }

    private sealed record Scope(
        string ProjectHandle,
        string DeliveryId,
        DateTimeOffset ExpiresAtUtc);
}
