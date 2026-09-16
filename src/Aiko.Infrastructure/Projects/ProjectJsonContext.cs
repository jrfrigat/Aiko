using System.Text.Json.Serialization;
using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
using Aiko.Domain.Execution;
using Aiko.Domain.Workflow;

namespace Aiko.Infrastructure.Projects;

/// <summary>
/// Source-generated JSON context for reading and writing .aiko documents
/// in camelCase with string enum values.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ProjectManifest))]
[JsonSerializable(typeof(WorkflowDefinition))]
[JsonSerializable(typeof(BoardProjectionDefinition))]
[JsonSerializable(typeof(Card))]
[JsonSerializable(typeof(CardRelation))]
[JsonSerializable(typeof(RelationDocument))]
[JsonSerializable(typeof(StageExecution))]
internal sealed partial class ProjectJsonContext : JsonSerializerContext;
