using System.Text.Json.Serialization;
using Aiko.Infrastructure.Logging;

namespace Aiko.Infrastructure.Storage;

/// <summary>
/// Source-generated JSON context for daemon settings (camelCase).
/// </summary>
/// <remarks>
/// A section the file does not state is left out of what is written rather than written as null: the file
/// stays what it says, and a reader that predates a section keeps reading the rest of it.
/// </remarks>
[JsonSerializable(typeof(DaemonEndpointSettings))]
[JsonSerializable(typeof(LogRetentionSettings))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class EndpointJsonContext : JsonSerializerContext;
