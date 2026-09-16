using System.Text.Json.Serialization;
using Aiko.Application.Agents;
using Aiko.Application.Contracts;
using Aiko.Application.Prioritization;
using Aiko.Domain.Cards;
using Aiko.Domain.Execution;
using Aiko.Domain.Workflow;

namespace Aiko.Server.Contracts;

/// <summary>
/// Source-generated JSON context of the daemon HTTP API (camelCase, string enum values).
/// </summary>
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(SystemResponse))]
[JsonSerializable(typeof(RegisteredProject))]
[JsonSerializable(typeof(RegisteredProject[]))]
[JsonSerializable(typeof(IReadOnlyList<RegisteredProject>))]
[JsonSerializable(typeof(InitializeProjectRequest))]
[JsonSerializable(typeof(ReindexResult))]
[JsonSerializable(typeof(Card))]
[JsonSerializable(typeof(IReadOnlyList<Card>))]
[JsonSerializable(typeof(CardRelation))]
[JsonSerializable(typeof(IReadOnlyList<CardRelation>))]
[JsonSerializable(typeof(WorkflowDefinition))]
[JsonSerializable(typeof(IReadOnlyList<WorkflowDefinition>))]
[JsonSerializable(typeof(BoardProjectionDefinition))]
[JsonSerializable(typeof(IReadOnlyList<BoardProjectionDefinition>))]
[JsonSerializable(typeof(ProjectBoardSnapshot))]
[JsonSerializable(typeof(CardPriority))]
[JsonSerializable(typeof(IReadOnlyList<CardPriority>))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(AppSettingsView))]
[JsonSerializable(typeof(AikoEvent))]
[JsonSerializable(typeof(IReadOnlyList<AikoEvent>))]
[JsonSerializable(typeof(MoveCardRequest))]
[JsonSerializable(typeof(UpdateWorkflowRequest))]
[JsonSerializable(typeof(UpdateCardRequest))]
[JsonSerializable(typeof(CreateCardRequest))]
[JsonSerializable(typeof(PairRequest))]
[JsonSerializable(typeof(PairResponse))]
[JsonSerializable(typeof(UpdateArtifactRequest))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(RevisionConflictResponse))]
[JsonSerializable(typeof(ArtifactConflictResponse))]
[JsonSerializable(typeof(CardArtifactSummary))]
[JsonSerializable(typeof(IReadOnlyList<CardArtifactSummary>))]
[JsonSerializable(typeof(CardArtifactDocument))]
[JsonSerializable(typeof(IReadOnlyList<MemoryDocument>))]
[JsonSerializable(typeof(StageExecution))]
[JsonSerializable(typeof(IReadOnlyList<StageExecution>))]
[JsonSerializable(typeof(AgentAdapterOption))]
[JsonSerializable(typeof(IReadOnlyList<AgentAdapterOption>))]
[JsonSerializable(typeof(PlanAgentInstallationRequest))]
[JsonSerializable(typeof(UnifiedInstallationPlan))]
[JsonSerializable(typeof(InstallationFileResult))]
[JsonSerializable(typeof(AgentInstallationResult))]
[JsonSerializable(typeof(UnifiedInstallationResult))]
[JsonSerializable(typeof(UnifiedUninstallationPlan))]
[JsonSerializable(typeof(UnifiedUninstallationResult))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
internal sealed partial class ServerJsonContext : JsonSerializerContext;
