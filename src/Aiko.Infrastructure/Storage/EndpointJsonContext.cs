using System.Text.Json.Serialization;

namespace Aiko.Infrastructure.Storage;

/// <summary>
/// Source-generated JSON context for daemon settings (camelCase).
/// </summary>
[JsonSerializable(typeof(DaemonEndpointSettings))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class EndpointJsonContext : JsonSerializerContext;
