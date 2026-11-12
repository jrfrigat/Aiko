using System.Text.Json;
using System.Text.Json.Serialization;
using Aiko.Domain.Execution;

namespace Aiko.Application.Agents;

/// <summary>
/// One stored layer of an agent's configuration. A null member means "not stated here", so merging a layer
/// never confuses a default with a choice somebody made.
/// </summary>
/// <param name="SchemaVersion">Version of the document format.</param>
/// <param name="Id">Instance identifier the document belongs to.</param>
/// <param name="AdapterId">Adapter implementation the instance runs, or null for the default instance.</param>
/// <param name="DisplayName">Name the instance is shown with, or null to use the adapter's own.</param>
/// <param name="Enabled">Whether the dispatcher may route work to the agent.</param>
/// <param name="Transport">How commands reach the agent.</param>
/// <param name="Fallback">Transport used when the chosen one is unavailable; only <c>command</c>.</param>
/// <param name="Priority">Order among equally capable agents; higher goes first.</param>
/// <param name="Capabilities">What the agent can do.</param>
/// <param name="Acp">ACP launch settings.</param>
/// <param name="Sdk">SDK adapter settings.</param>
/// <param name="CommandClaim">Whether the agent may still take commands from the queue.</param>
public sealed record AgentConfig(
    int SchemaVersion = AgentConfig.CurrentSchemaVersion,
    string? Id = null,
    string? AdapterId = null,
    string? DisplayName = null,
    bool? Enabled = null,
    AgentTransport? Transport = null,
    AgentTransport? Fallback = null,
    int? Priority = null,
    AgentCapabilityProfile? Capabilities = null,
    AgentAcpConfig? Acp = null,
    AgentSdkConfig? Sdk = null,
    bool? CommandClaim = null)
{
    /// <summary>Version written by the current application version.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// A stored environment value that means "keep the value from the layer below". The API masks secrets
    /// with it, and the merge never lets it replace a real value.
    /// </summary>
    public const string SecretSentinel = "***";

    /// <summary>Fields this version does not know, kept so a newer document survives a round-trip.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }

    /// <summary>The defaults an adapter contributes before any stored layer is applied.</summary>
    public static AgentConfig Default(string adapterId) => new(
        Id: adapterId,
        AdapterId: null,
        Enabled: true,
        Transport: AgentTransport.Command,
        Fallback: AgentTransport.Command,
        Priority: 0,
        Capabilities: AgentCapabilityProfile.Default,
        CommandClaim: true);
}
