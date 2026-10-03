using System.Text.Json.Serialization;
using Aiko.Application.Contracts;

namespace Aiko.Infrastructure.Memory;

/// <summary>
/// Source-generated JSON context for the memory candidate documents beside a card.
/// </summary>
/// <remarks>
/// A context of its own rather than an entry in the shared <c>ProjectJsonContext</c>: the candidate
/// document belongs to the memory store alone, and a document nobody else touches has no reason to
/// enlarge the shared context every card and relation already pays for. The naming, indentation and enum
/// style are the same, so the shapes cannot drift apart.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(MemoryCandidate))]
internal sealed partial class MemoryCandidateJsonContext : JsonSerializerContext;
