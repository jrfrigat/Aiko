using System.Text.Json;
using System.Text.Json.Serialization;
using Aiko.Application.Contracts;

namespace Aiko.Infrastructure.Settings;

/// <summary>
/// Source-generated JSON context for settings files (camelCase, string enum values).
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
