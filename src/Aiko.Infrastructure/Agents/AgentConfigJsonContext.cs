using System.Text.Json.Serialization;
using Aiko.Application.Agents;

namespace Aiko.Infrastructure.Agents;

/// <summary>
/// Source-generated JSON context for agent configuration files (camelCase, string enum values).
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AgentConfig))]
internal sealed partial class AgentConfigJsonContext : JsonSerializerContext;
