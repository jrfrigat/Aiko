namespace Aiko.Application.Agents;

/// <summary>
/// The two sides of a configuration that crosses the HTTP boundary: every environment value is masked on
/// the way out, and the sentinel a caller sends back keeps the value already stored for that layer.
/// </summary>
public static class AgentSecretMasking
{
    /// <summary>Masks every environment value of one effective configuration.</summary>
    public static EffectiveAgentConfig Mask(EffectiveAgentConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return config with
        {
            Acp = MaskAcp(config.Acp),
            Sdk = MaskSdk(config.Sdk)
        };
    }

    /// <summary>Masks every environment value of a list of effective configurations.</summary>
    public static IReadOnlyList<EffectiveAgentConfig> Mask(
        IReadOnlyList<EffectiveAgentConfig> configs)
    {
        ArgumentNullException.ThrowIfNull(configs);
        return [.. configs.Select(Mask)];
    }

    /// <summary>
    /// Replaces every sentinel in the incoming document with the value stored in the same layer, so a
    /// masked value a caller sends back does not erase the secret behind it.
    /// </summary>
    public static AgentConfig KeepStoredValues(AgentConfig? stored, AgentConfig incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        return incoming with
        {
            Acp = KeepStoredAcp(stored?.Acp, incoming.Acp),
            Sdk = KeepStoredSdk(stored?.Sdk, incoming.Sdk)
        };
    }

    private static AgentAcpConfig? MaskAcp(AgentAcpConfig? acp) =>
        acp is null ? null : acp with { Environment = MaskValues(acp.Environment) };

    private static AgentSdkConfig? MaskSdk(AgentSdkConfig? sdk) =>
        sdk is null ? null : sdk with { Environment = MaskValues(sdk.Environment) };

    private static AgentAcpConfig? KeepStoredAcp(AgentAcpConfig? stored, AgentAcpConfig? incoming) =>
        incoming is null
            ? null
            : incoming with { Environment = KeepStoredEnvironment(stored?.Environment, incoming.Environment) };

    private static AgentSdkConfig? KeepStoredSdk(AgentSdkConfig? stored, AgentSdkConfig? incoming) =>
        incoming is null
            ? null
            : incoming with { Environment = KeepStoredEnvironment(stored?.Environment, incoming.Environment) };

    private static IReadOnlyDictionary<string, string>? MaskValues(
        IReadOnlyDictionary<string, string>? values) =>
        values is null
            ? null
            : values.ToDictionary(
                pair => pair.Key,
                _ => AgentConfig.SecretSentinel,
                StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, string>? KeepStoredEnvironment(
        IReadOnlyDictionary<string, string>? stored,
        IReadOnlyDictionary<string, string>? incoming)
    {
        if (incoming is null)
        {
            return null;
        }

        var kept = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in incoming)
        {
            if (StringComparer.Ordinal.Equals(pair.Value, AgentConfig.SecretSentinel))
            {
                if (stored is not null && stored.TryGetValue(pair.Key, out var value))
                {
                    kept[pair.Key] = value;
                }

                continue;
            }

            kept[pair.Key] = pair.Value;
        }

        return kept;
    }
}
