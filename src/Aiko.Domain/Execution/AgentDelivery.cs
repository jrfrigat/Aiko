using System.Text.Json.Serialization;

namespace Aiko.Domain.Execution;

/// <summary>One attempt to deliver a queued command to an agent directly.</summary>
/// <param name="Id">Identifier of the attempt, unique within the project.</param>
/// <param name="CommandId">Command being delivered.</param>
/// <param name="AgentAdapterId">Agent the attempt was made with.</param>
/// <param name="Transport">Transport used for the attempt.</param>
/// <param name="State">Where the attempt got to.</param>
/// <param name="SessionId">Transport session, when one was opened.</param>
/// <param name="ExecutionId">Stage execution the agent started, when it reported one.</param>
/// <param name="FailureReason">Why the attempt did not finish, when it did not.</param>
/// <param name="CreatedAtUtc">When the attempt was created.</param>
/// <param name="StartedAtUtc">When the transport started it, or null.</param>
/// <param name="FinishedAtUtc">When it stopped being open, or null.</param>
/// <param name="AgentInstanceId">Configured agent instance the attempt ran as, or null on an old record.</param>
/// <param name="AdapterId">Adapter that ran the attempt, or null on an old record.</param>
public sealed record AgentDelivery(
    string Id,
    string CommandId,
    string AgentAdapterId,
    AgentTransport Transport,
    AgentDeliveryState State,
    string? SessionId,
    string? ExecutionId,
    string? FailureReason,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    string? AgentInstanceId = null,
    string? AdapterId = null)
{
    /// <summary>True while the attempt may still produce work.</summary>
    [JsonIgnore]
    public bool IsOpen => State is AgentDeliveryState.Starting or AgentDeliveryState.Running;

    /// <summary>The instance the attempt ran as; an old record's agent id is its default instance.</summary>
    [JsonIgnore]
    public string ResolvedInstanceId => AgentInstanceId ?? AgentAdapterId;

    /// <summary>The adapter that ran the attempt; an old record's agent id is the adapter id.</summary>
    [JsonIgnore]
    public string ResolvedAdapterId => AdapterId ?? AgentAdapterId;
}

/// <summary>The <c>deliveries.json</c> document: the direct-delivery history of one project.</summary>
/// <param name="SchemaVersion">Schema version of the file.</param>
/// <param name="NextSequence">Number the next delivery's identifier takes.</param>
/// <param name="Entries">The attempts, oldest first.</param>
public sealed record AgentDeliveryDocument(
    int SchemaVersion,
    int NextSequence,
    IReadOnlyList<AgentDelivery> Entries);
