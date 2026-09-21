using System.Text.Json.Serialization;
using Aiko.Application.Contracts;

namespace Aiko.Infrastructure.Releases;

/// <summary>
/// Source-generated JSON context for the release history, written the way every other <c>.aiko</c> document is:
/// camelCase names, indentation and string values for anything enumerated.
/// </summary>
/// <remarks>
/// A context of its own rather than a member of <c>ProjectJsonContext</c>: the file is read and written by this
/// store alone, and a document nobody else touches has no reason to enlarge the shared context that every card
/// and relation already pays for.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ReleaseDocument))]
internal sealed partial class ReleasesJsonContext : JsonSerializerContext;
