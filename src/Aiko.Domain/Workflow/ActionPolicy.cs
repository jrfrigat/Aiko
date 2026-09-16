using System.Text.Json.Serialization;

namespace Aiko.Domain.Workflow;

/// <summary>
/// Policy for performing a potentially dangerous action.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ActionPolicy>))]
public enum ActionPolicy
{
    /// <summary>Allow without asking.</summary>
    Allow,

    /// <summary>Ask the user before performing.</summary>
    Ask,

    /// <summary>Deny.</summary>
    Deny
}
