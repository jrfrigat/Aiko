using Aiko.Application.Agents;
using Aiko.Domain.Execution;

namespace Aiko.Infrastructure.Agents;

/// <summary>
/// Merges the adapter default, the user layer and the project layer of one agent instance, and refuses
/// documents that name a transport or a capability the adapter does not have.
/// </summary>
public sealed class AgentConfigService(
    IEnumerable<IAgentAdapter> adapters,
    IAgentConfigStore store) : IAgentConfigService
{
    private static readonly IReadOnlySet<string> AikoAccessValues =
        new HashSet<string>(["mcp", "rest", "cli"], StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlySet<string> ActionValues =
        new HashSet<string>(
            ["start", "pause", "resume", "answer", "run-board", "release"],
            StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlySet<string> PermissionModeValues =
        new HashSet<string>(
            ["read-only", "workspace-write", "full-access"],
            StringComparer.OrdinalIgnoreCase);

    private static readonly string[] SourceKeys =
    [
        "adapterId",
        "displayName",
        "enabled",
        "transport",
        "fallback",
        "priority",
        "capabilities.aikoAccess",
        "capabilities.actions",
        "capabilities.stages",
        "capabilities.cardTypes",
        "capabilities.permissionModes",
        "capabilities.maxConcurrentSessions",
        "capabilities.resume",
        "capabilities.structuredOutput",
        "acp",
        "sdk",
        "commandClaim"
    ];

    private readonly IReadOnlyDictionary<string, IAgentAdapter> adaptersById = adapters
        .ToDictionary(adapter => adapter.Id, StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async ValueTask<EffectiveAgentConfig> GetEffectiveAsync(
        string? projectId,
        string instanceId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        var id = instanceId.Trim();
        var user = await store.ReadUserAsync(id, cancellationToken);
        var project = string.IsNullOrWhiteSpace(projectId)
            ? null
            : await store.ReadProjectAsync(projectId, id, cancellationToken);
        var adapterId = user?.AdapterId ?? project?.AdapterId;
        if (string.IsNullOrWhiteSpace(adapterId))
        {
            if (!adaptersById.ContainsKey(id))
            {
                throw new ArgumentException(
                    $"Unknown agent '{id}': it has no configuration and no adapter carries that id.",
                    nameof(instanceId));
            }

            adapterId = id;
        }

        var adapter = GetAdapter(adapterId);
        var effective = Merge(adapter, id, user, project);
        Validate(effective);
        return effective;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<EffectiveAgentConfig>> ListEffectiveAsync(
        string? projectId,
        CancellationToken cancellationToken)
    {
        var ids = new HashSet<string>(adaptersById.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var id in await store.ListUserAsync(cancellationToken))
        {
            ids.Add(id);
        }

        if (!string.IsNullOrWhiteSpace(projectId))
        {
            foreach (var id in await store.ListProjectAsync(projectId, cancellationToken))
            {
                ids.Add(id);
            }
        }

        var results = new List<EffectiveAgentConfig>();
        foreach (var id in ids)
        {
            try
            {
                results.Add(await GetEffectiveAsync(projectId, id, cancellationToken));
            }
            catch (Exception exception) when (
                exception is ArgumentException or InvalidOperationException)
            {
                // A document that names an unknown adapter or an invalid transport is left out of the
                // list; the file stays where the person can fix or remove it.
            }
        }

        return [.. results
            .OrderBy(item => item.AdapterId ?? item.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)];
    }

    /// <inheritdoc />
    public async ValueTask SaveUserAsync(AgentConfig config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);
        var adapter = ValidateLayer(config);
        var instanceId = config.Id!.Trim();
        Validate(Merge(adapter, instanceId, config, null));
        await store.SaveUserAsync(
            config with
            {
                SchemaVersion = AgentConfig.CurrentSchemaVersion,
                Id = instanceId,
                AdapterId = Trim(config.AdapterId)
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask SaveProjectAsync(
        string projectId,
        AgentConfig config,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);
        var adapter = ValidateLayer(config);
        var instanceId = config.Id!.Trim();
        var user = await store.ReadUserAsync(instanceId, cancellationToken);
        Validate(Merge(adapter, instanceId, user, config));
        await store.SaveProjectAsync(
            projectId,
            config with
            {
                SchemaVersion = AgentConfig.CurrentSchemaVersion,
                Id = instanceId,
                AdapterId = Trim(config.AdapterId)
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public void Validate(EffectiveAgentConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!config.SupportedTransports.Contains(config.Transport))
        {
            throw new InvalidOperationException(
                $"Agent '{config.Id}' does not support transport '{config.Transport}'. " +
                $"Supported: {string.Join(", ", config.SupportedTransports)}.");
        }

        if (config.Fallback != AgentTransport.Command)
        {
            throw new InvalidOperationException(
                "Only the command queue can serve as a fallback transport.");
        }

        if (config.Transport == AgentTransport.Acp &&
            string.IsNullOrWhiteSpace(config.Acp?.Executable))
        {
            throw new InvalidOperationException(
                $"Agent '{config.Id}' uses ACP but states no executable.");
        }

        if (config.Transport == AgentTransport.Sdk &&
            string.IsNullOrWhiteSpace(config.Sdk?.Adapter))
        {
            throw new InvalidOperationException(
                $"Agent '{config.Id}' uses an SDK but states no adapter.");
        }

        var capabilities = config.Capabilities;
        if (capabilities.MaxConcurrentSessions is < 1)
        {
            throw new InvalidOperationException(
                $"Agent '{config.Id}' needs at least one session slot.");
        }

        ValidateValues(config.Id, "aikoAccess", capabilities.AikoAccess, AikoAccessValues, false);
        ValidateValues(config.Id, "actions", capabilities.Actions, ActionValues, true);
        ValidateValues(
            config.Id,
            "permissionModes",
            capabilities.PermissionModes,
            PermissionModeValues,
            true);

        if (capabilities.Stages is null || capabilities.Stages.Count == 0)
        {
            throw new InvalidOperationException(
                $"Agent '{config.Id}' states no stages; use '*' for every stage.");
        }

        if (capabilities.CardTypes is null || capabilities.CardTypes.Count == 0)
        {
            throw new InvalidOperationException(
                $"Agent '{config.Id}' states no card types; use '*' for every type.");
        }

        ValidateEnvironment(config.Id, config.Acp?.Environment);
        ValidateEnvironment(config.Id, config.Sdk?.Environment);
    }

    private static EffectiveAgentConfig Merge(
        IAgentAdapter adapter,
        string instanceId,
        AgentConfig? user,
        AgentConfig? project)
    {
        var effective = adapter.DefaultConfig;
        var sources = new Dictionary<string, AgentConfigSource>(StringComparer.Ordinal);
        var displayName = StringComparer.OrdinalIgnoreCase.Equals(instanceId, adapter.Id)
            ? adapter.DisplayName
            : instanceId;
        sources["adapterId"] = AgentConfigSource.Default;
        sources["displayName"] = AgentConfigSource.Default;
        Apply(user, AgentConfigSource.User);
        Apply(project, AgentConfigSource.Project);

        foreach (var key in SourceKeys)
        {
            sources.TryAdd(key, AgentConfigSource.Default);
        }

        var supported = SupportedTransports(adapter);
        return new EffectiveAgentConfig(
            instanceId,
            effective.Enabled ?? true,
            effective.Transport ?? AgentTransport.Command,
            effective.Fallback ?? AgentTransport.Command,
            effective.Priority ?? 0,
            effective.Capabilities ?? AgentCapabilityProfile.Default,
            effective.Acp,
            effective.Sdk,
            effective.CommandClaim ?? true,
            supported,
            sources,
            AdapterId: adapter.Id,
            DisplayName: displayName);

        void Apply(AgentConfig? layer, AgentConfigSource source)
        {
            if (layer is null)
            {
                return;
            }

            if (layer.Id is { Length: > 0 } id &&
                !StringComparer.OrdinalIgnoreCase.Equals(id.Trim(), instanceId))
            {
                throw new InvalidOperationException(
                    $"An agent configuration for '{instanceId}' cannot carry id '{id}'.");
            }

            if (layer.AdapterId is { Length: > 0 } adapterId &&
                !StringComparer.OrdinalIgnoreCase.Equals(adapterId.Trim(), adapter.Id))
            {
                throw new InvalidOperationException(
                    $"Agent '{instanceId}' cannot belong to adapter '{adapterId}': " +
                    $"its configuration resolves to '{adapter.Id}'.");
            }

            if (layer.AdapterId is { Length: > 0 })
            {
                sources["adapterId"] = source;
            }

            if (layer.DisplayName is { Length: > 0 } name)
            {
                displayName = name.Trim();
                sources["displayName"] = source;
            }

            if (layer.Enabled is { } enabled)
            {
                effective = effective with { Enabled = enabled };
                sources["enabled"] = source;
            }

            if (layer.Transport is { } transport)
            {
                effective = effective with { Transport = transport };
                sources["transport"] = source;
            }

            if (layer.Fallback is { } fallback)
            {
                effective = effective with { Fallback = fallback };
                sources["fallback"] = source;
            }

            if (layer.Priority is { } priority)
            {
                effective = effective with { Priority = priority };
                sources["priority"] = source;
            }

            if (layer.Capabilities is { } capabilities)
            {
                effective = effective with
                {
                    Capabilities = MergeCapabilities(
                        effective.Capabilities ?? AgentCapabilityProfile.Default,
                        capabilities)
                };
                AddCapabilitySources(capabilities, source);
            }

            if (layer.Acp is { } acp)
            {
                effective = effective with { Acp = MergeAcp(effective.Acp, acp) };
                sources["acp"] = source;
            }

            if (layer.Sdk is { } sdk)
            {
                effective = effective with { Sdk = MergeSdk(effective.Sdk, sdk) };
                sources["sdk"] = source;
            }

            if (layer.CommandClaim is { } commandClaim)
            {
                effective = effective with { CommandClaim = commandClaim };
                sources["commandClaim"] = source;
            }
        }

        void AddCapabilitySources(AgentCapabilityProfile capabilities, AgentConfigSource source)
        {
            if (capabilities.AikoAccess is not null)
            {
                sources["capabilities.aikoAccess"] = source;
            }

            if (capabilities.Actions is not null)
            {
                sources["capabilities.actions"] = source;
            }

            if (capabilities.Stages is not null)
            {
                sources["capabilities.stages"] = source;
            }

            if (capabilities.CardTypes is not null)
            {
                sources["capabilities.cardTypes"] = source;
            }

            if (capabilities.PermissionModes is not null)
            {
                sources["capabilities.permissionModes"] = source;
            }

            if (capabilities.MaxConcurrentSessions is not null)
            {
                sources["capabilities.maxConcurrentSessions"] = source;
            }

            if (capabilities.Resume is not null)
            {
                sources["capabilities.resume"] = source;
            }

            if (capabilities.StructuredOutput is not null)
            {
                sources["capabilities.structuredOutput"] = source;
            }
        }
    }

    private static AgentAcpConfig MergeAcp(AgentAcpConfig? current, AgentAcpConfig layer) => new(
        Executable: layer.Executable ?? current?.Executable,
        Args: layer.Args ?? current?.Args,
        Protocol: layer.Protocol,
        StartTimeoutSeconds: layer.StartTimeoutSeconds,
        TurnTimeoutMinutes: layer.TurnTimeoutMinutes,
        Environment: MergeEnvironment(current?.Environment, layer.Environment));

    private static AgentSdkConfig MergeSdk(AgentSdkConfig? current, AgentSdkConfig layer) => new(
        Adapter: layer.Adapter ?? current?.Adapter,
        Environment: MergeEnvironment(current?.Environment, layer.Environment));

    private static IReadOnlyDictionary<string, string>? MergeEnvironment(
        IReadOnlyDictionary<string, string>? current,
        IReadOnlyDictionary<string, string>? layer)
    {
        if (layer is null)
        {
            return current;
        }

        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (current is not null)
        {
            foreach (var pair in current)
            {
                merged[pair.Key] = pair.Value;
            }
        }

        foreach (var pair in layer)
        {
            if (StringComparer.Ordinal.Equals(pair.Value, AgentConfig.SecretSentinel))
            {
                continue;
            }

            merged[pair.Key] = pair.Value;
        }

        return merged;
    }

    private static AgentCapabilityProfile MergeCapabilities(
        AgentCapabilityProfile current,
        AgentCapabilityProfile layer) => new(
        AikoAccess: layer.AikoAccess ?? current.AikoAccess,
        Actions: layer.Actions ?? current.Actions,
        Stages: layer.Stages ?? current.Stages,
        CardTypes: layer.CardTypes ?? current.CardTypes,
        PermissionModes: layer.PermissionModes ?? current.PermissionModes,
        MaxConcurrentSessions: layer.MaxConcurrentSessions ?? current.MaxConcurrentSessions,
        Resume: layer.Resume ?? current.Resume,
        StructuredOutput: layer.StructuredOutput ?? current.StructuredOutput);

    private static IReadOnlyList<AgentTransport> SupportedTransports(IAgentAdapter adapter)
    {
        var transports = new List<AgentTransport> { AgentTransport.Command };
        if (adapter.Capabilities.HasFlag(AgentCapabilities.AcpTransport))
        {
            transports.Add(AgentTransport.Acp);
        }

        if (adapter.Capabilities.HasFlag(AgentCapabilities.SdkTransport))
        {
            transports.Add(AgentTransport.Sdk);
        }

        return transports;
    }

    private static void ValidateEnvironment(
        string instanceId,
        IReadOnlyDictionary<string, string>? environment)
    {
        if (environment is null)
        {
            return;
        }

        foreach (var pair in environment)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) ||
                pair.Key.Contains('=', StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Agent '{instanceId}' states an invalid environment variable name '{pair.Key}'.");
            }

            if (pair.Key.StartsWith("AIKO_", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Agent '{instanceId}' cannot set Aiko's own environment variable '{pair.Key}'.");
            }
        }
    }

    private static void ValidateValues(
        string instanceId,
        string name,
        IReadOnlyList<string>? values,
        IReadOnlySet<string> known,
        bool allowAny)
    {
        if (values is null)
        {
            return;
        }

        if (values.Count == 0)
        {
            throw new InvalidOperationException(
                $"Agent '{instanceId}' states an empty '{name}' list.");
        }

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"Agent '{instanceId}' states an empty value in '{name}'.");
            }

            if (allowAny && value == "*")
            {
                continue;
            }

            if (!known.Contains(value))
            {
                throw new InvalidOperationException(
                    $"Agent '{instanceId}' states unknown {name} value '{value}'.");
            }
        }
    }

    private IAgentAdapter ValidateLayer(AgentConfig config)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(config.Id);
        var instanceId = config.Id.Trim();
        var adapterId = config.AdapterId?.Trim();
        if (string.IsNullOrWhiteSpace(adapterId))
        {
            if (!adaptersById.ContainsKey(instanceId))
            {
                throw new ArgumentException(
                    $"Agent '{instanceId}' must name an AdapterId: '{instanceId}' is not an adapter.",
                    nameof(config));
            }

            adapterId = instanceId;
        }

        var adapter = GetAdapter(adapterId);
        if (adaptersById.ContainsKey(instanceId) &&
            !StringComparer.OrdinalIgnoreCase.Equals(instanceId, adapter.Id))
        {
            throw new ArgumentException(
                $"Instance '{instanceId}' carries an adapter id and cannot belong to '{adapter.Id}'.",
                nameof(config));
        }

        if (config.Transport is { } transport &&
            !SupportedTransports(adapter).Contains(transport))
        {
            throw new InvalidOperationException(
                $"Agent '{adapter.Id}' does not support transport '{transport}'.");
        }

        if (config.Fallback is { } fallback && fallback != AgentTransport.Command)
        {
            throw new InvalidOperationException(
                "Only the command queue can serve as a fallback transport.");
        }

        if (config.Capabilities is { } capabilities)
        {
            if (capabilities.MaxConcurrentSessions is < 1)
            {
                throw new InvalidOperationException(
                    $"Agent '{adapter.Id}' needs at least one session slot.");
            }

            ValidateValues(adapter.Id, "aikoAccess", capabilities.AikoAccess, AikoAccessValues, false);
            ValidateValues(adapter.Id, "actions", capabilities.Actions, ActionValues, true);
            ValidateValues(
                adapter.Id,
                "permissionModes",
                capabilities.PermissionModes,
                PermissionModeValues,
                true);
        }

        if (config.Acp is { } acp)
        {
            if (acp.Executable is not null && string.IsNullOrWhiteSpace(acp.Executable))
            {
                throw new InvalidOperationException(
                    $"Agent '{adapter.Id}' states an empty ACP executable.");
            }

            if (acp.StartTimeoutSeconds < 1 || acp.TurnTimeoutMinutes < 1)
            {
                throw new InvalidOperationException(
                    $"Agent '{adapter.Id}' states a non-positive ACP timeout.");
            }

            ValidateEnvironment(instanceId, acp.Environment);
        }

        if (config.Sdk is { } sdk)
        {
            if (sdk.Adapter is not null && string.IsNullOrWhiteSpace(sdk.Adapter))
            {
                throw new InvalidOperationException(
                    $"Agent '{adapter.Id}' states an empty SDK adapter.");
            }

            ValidateEnvironment(instanceId, sdk.Environment);
        }

        return adapter;
    }

    private IAgentAdapter GetAdapter(string? adapterId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adapterId);
        var trimmed = adapterId.Trim();
        return adaptersById.TryGetValue(trimmed, out var adapter)
            ? adapter
            : throw new ArgumentException(
                $"Unknown agent adapter '{trimmed}'. Known: {string.Join(", ", adaptersById.Keys)}.",
                nameof(adapterId));
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
