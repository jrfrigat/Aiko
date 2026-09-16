using System.Text.Json.Serialization;

namespace Aiko.Domain.Workflow;

/// <summary>
/// Pipeline reaction to a missing required stage artifact.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<MissingArtifactPolicy>))]
public enum MissingArtifactPolicy
{
    /// <summary>The artifact is optional.</summary>
    Allow,

    /// <summary>Record a warning.</summary>
    Warn,

    /// <summary>Restart the stage.</summary>
    Retry,

    /// <summary>Move the execution to the needs-attention state.</summary>
    NeedsAttention,

    /// <summary>Forbid automatic advance to the next stage.</summary>
    BlockAutoAdvance
}
