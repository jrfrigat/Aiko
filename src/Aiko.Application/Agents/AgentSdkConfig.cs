namespace Aiko.Application.Agents;

/// <summary>How Aiko starts an agent through an SDK adapter.</summary>
/// <param name="Adapter">Identifier of the SDK adapter, or null when the transport is not used.</param>
/// <param name="Environment">Environment variables added to the SDK run.</param>
public sealed record AgentSdkConfig(
    string? Adapter = null,
    IReadOnlyDictionary<string, string>? Environment = null);
