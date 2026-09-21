using System.Text.Json;
using System.Text.Json.Serialization;
using Aiko.Application.Installation;

namespace Aiko.Infrastructure.Installation;

/// <summary>
/// Source-generated JSON context for <c>install.json</c> (camelCase, indented, string enum values).
/// </summary>
/// <remarks>
/// A context rather than reflection, like every other JSON surface here: the file is written and read by the
/// same binary, so the shape of it is a contract between two builds of Aiko, and generating the code is what
/// keeps that contract visible when the record changes.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(InstalledVersion))]
internal sealed partial class InstallationJsonContext : JsonSerializerContext;
