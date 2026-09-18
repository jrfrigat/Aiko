using System.Text.Json.Serialization;
using Aiko.Application.Agents;
using Aiko.Application.Contracts;
using Aiko.Application.Prioritization;
using Aiko.Domain.Cards;
using Aiko.Domain.Execution;
using Aiko.Domain.Prioritization;
using Aiko.Domain.Workflow;
using Aiko.Pwa.Contracts;

namespace Aiko.Pwa.Services;

/// <summary>
/// Source-generated JSON contract of the PWA. The published client is trimmed, and reflection-based
/// serialization cannot resolve constructor parameter names there ("ConstructorContainsNullParameter
/// Names"), which broke the board, project registration and settings in the Release PWA. Every type
/// the UI sends or reads must therefore be listed here, and every HTTP call must pass
/// <see cref="PwaJson.Options"/>.
/// </summary>
[JsonSerializable(typeof(InitializeProjectRequest))]
[JsonSerializable(typeof(RegisteredProject))]
[JsonSerializable(typeof(IReadOnlyList<RegisteredProject>))]
[JsonSerializable(typeof(ReindexResult))]
[JsonSerializable(typeof(ProjectBoardSnapshot))]
[JsonSerializable(typeof(Card))]
[JsonSerializable(typeof(IReadOnlyList<Card>))]
[JsonSerializable(typeof(CardRelation))]
[JsonSerializable(typeof(IReadOnlyList<CardRelation>))]
[JsonSerializable(typeof(CardPriority))]
[JsonSerializable(typeof(IReadOnlyList<CardPriority>))]
[JsonSerializable(typeof(WorkflowDefinition))]
[JsonSerializable(typeof(IReadOnlyList<WorkflowDefinition>))]
[JsonSerializable(typeof(BoardProjectionDefinition))]
[JsonSerializable(typeof(IReadOnlyList<BoardProjectionDefinition>))]
[JsonSerializable(typeof(StageDefinition))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(AppSettingsView))]
[JsonSerializable(typeof(ExecutionSettings))]
[JsonSerializable(typeof(PrioritySettings))]
[JsonSerializable(typeof(StageExecution))]
[JsonSerializable(typeof(IReadOnlyList<StageExecution>))]
[JsonSerializable(typeof(CardArtifactSummary))]
[JsonSerializable(typeof(IReadOnlyList<CardArtifactSummary>))]
[JsonSerializable(typeof(CardArtifactDocument))]
[JsonSerializable(typeof(AikoEvent))]
[JsonSerializable(typeof(CreateCardRequest))]
[JsonSerializable(typeof(EstimateCardRequest))]
[JsonSerializable(typeof(RenameWorkflowRequest))]
[JsonSerializable(typeof(CreateTemplateRequest))]
[JsonSerializable(typeof(CreateTemplateFromProjectRequest))]
[JsonSerializable(typeof(UpdateTemplateRequest))]
[JsonSerializable(typeof(ExportTemplateRequest))]
[JsonSerializable(typeof(ImportTemplateRequest))]
[JsonSerializable(typeof(ApplyTemplateRequest))]
[JsonSerializable(typeof(MoveCardRequest))]
[JsonSerializable(typeof(UpdateCardRequest))]
[JsonSerializable(typeof(UpdateArtifactRequest))]
[JsonSerializable(typeof(UpdateWorkflowRequest))]
[JsonSerializable(typeof(CreateWorkflowRequest))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(SystemInfo))]
[JsonSerializable(typeof(PairCodeResponse))]
[JsonSerializable(typeof(ProjectTemplateSummary))]
[JsonSerializable(typeof(IReadOnlyList<ProjectTemplateSummary>))]
[JsonSerializable(typeof(ProjectTemplate))]
[JsonSerializable(typeof(GitStatus))]
[JsonSerializable(typeof(GitOverview))]
[JsonSerializable(typeof(IReadOnlyList<GitCommit>))]
[JsonSerializable(typeof(GitCardDiff))]
[JsonSerializable(typeof(DaemonTelemetry))]
[JsonSerializable(typeof(ProjectAnalytics))]
[JsonSerializable(typeof(CardComment))]
[JsonSerializable(typeof(IReadOnlyList<CardComment>))]
[JsonSerializable(typeof(AddCommentRequest))]
[JsonSerializable(typeof(WorkshopDiagnostics))]
[JsonSerializable(typeof(DiagnosticFinding))]
[JsonSerializable(typeof(IReadOnlyList<DiagnosticFinding>))]
[JsonSerializable(typeof(ActivityDay))]
[JsonSerializable(typeof(IReadOnlyList<ActivityDay>))]
[JsonSerializable(typeof(DirectoryListing))]
[JsonSerializable(typeof(AgentAdapterOption))]
[JsonSerializable(typeof(IReadOnlyList<AgentAdapterOption>))]
[JsonSerializable(typeof(AgentUserScope))]
[JsonSerializable(typeof(AgentProjectConnection))]
[JsonSerializable(typeof(IReadOnlyList<AgentProjectConnection>))]
[JsonSerializable(typeof(ConnectProjectAgentsRequest))]
[JsonSerializable(typeof(AgentConnectionResponse))]
[JsonSerializable(typeof(AgentInstallation))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
internal sealed partial class PwaJsonContext : JsonSerializerContext;
