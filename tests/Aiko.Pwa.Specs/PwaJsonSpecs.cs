using System.Text.Json;
using Aiko.Application.Agents;
using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
using Aiko.Domain.Execution;
using Aiko.Domain.Workflow;
using Aiko.Pwa.Contracts;
using Aiko.Pwa.Services;
using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// Guards the JSON contract of the PWA. The published client is trimmed, where reflection-based
/// serialization cannot resolve constructor parameter names and fails at runtime
/// ("ConstructorContainsNullParameterNames"); the board, project registration and settings were all
/// broken this way in release v0.1.0. Every type the client exchanges must therefore be covered by
/// the source-generated <see cref="PwaJsonContext"/>, and every HTTP call must pass
/// <see cref="PwaJson.Options"/>.
/// </summary>
public sealed class PwaJsonSpecs
{
    [Fact]
    public void Context_covers_every_type_the_client_exchanges()
    {
        Type[] types =
        [
            typeof(InitializeProjectRequest),
            typeof(RegisteredProject),
            typeof(ProjectBoardSnapshot),
            typeof(Card),
            typeof(CardRelation),
            typeof(WorkflowDefinition),
            typeof(StageDefinition),
            typeof(BoardProjectionDefinition),
            typeof(AppSettings),
            typeof(AppSettingsView),
            typeof(StageExecution),
            typeof(CardArtifactSummary),
            typeof(CardArtifactDocument),
            typeof(AikoEvent),
            typeof(CreateCardRequest),
            typeof(EstimateCardRequest),
            typeof(RenameWorkflowRequest),
            typeof(MoveCardRequest),
            typeof(UpdateCardRequest),
            typeof(UpdateArtifactRequest),
            typeof(UpdateWorkflowRequest),
            typeof(ErrorResponse),
            typeof(SystemInfo),
            typeof(ActivityDay),
            typeof(IReadOnlyList<ActivityDay>),
            typeof(DirectoryListing),
            typeof(AgentAdapterOption),
            typeof(IReadOnlyList<AgentAdapterOption>),
            typeof(AgentInstallation)
        ];

        foreach (var type in types)
        {
            Assert.NotNull(PwaJsonContext.Default.GetTypeInfo(type));
        }

        // The mapped options must resolve metadata from the generated context: reflection-based
        // serialization is what fails in the trimmed Release client.
        Assert.NotNull(PwaJson.Options.TypeInfoResolver);
        foreach (var type in types)
        {
            Assert.NotNull(PwaJson.Options.TypeInfoResolver!.GetTypeInfo(type, PwaJson.Options));
        }
    }

    [Fact]
    public void Requests_use_the_camel_case_names_and_string_enums_of_the_daemon()
    {
        var json = JsonSerializer.Serialize(
            new InitializeProjectRequest("C:/work/demo", "Demo"),
            PwaJson.Options);

        Assert.Contains("\"rootPath\":\"C:/work/demo\"", json, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"Demo\"", json, StringComparison.Ordinal);
        // No policy in the request: null is how "the template's own policy decides" travels, so the
        // daemon's init takes the policy the chosen template carries.
        Assert.Contains("\"gitPolicy\":null", json, StringComparison.Ordinal);

        // A caller that does name one sends the enum's member name, not a number and not camelCase: the
        // daemon generates its own context with UseStringEnumConverter, so both sides speak "LocalOnly".
        var explicitPolicy = JsonSerializer.Serialize(
            new InitializeProjectRequest("C:/work/demo", null, ProjectGitPolicy.TrackProjectKnowledge),
            PwaJson.Options);
        Assert.Contains("\"gitPolicy\":\"TrackProjectKnowledge\"", explicitPolicy, StringComparison.Ordinal);

        var artifact = JsonSerializer.Serialize(
            new UpdateArtifactRequest("spec.md", "# Doc", "v1"),
            PwaJson.Options);
        Assert.Contains("\"expectedVersion\":\"v1\"", artifact, StringComparison.Ordinal);
    }

    [Fact]
    public void Board_snapshot_of_the_daemon_deserializes()
    {
        const string payload = """
            {
              "project": { "id": "p1", "name": "Demo", "rootPath": "C:/work/demo" },
              "workflows": [
                {
                  "id": "task",
                  "title": "Task",
                  "stages": [
                    {
                      "id": "backlog",
                      "title": "Backlog",
                      "order": 10,
                      "instruction": "wait",
                      "allowedCardKinds": [ "Task" ],
                      "defaultAgentAdapterId": null,
                      "requiredArtifacts": [],
                      "actionPolicies": {}
                    }
                  ],
                  "revision": 3
                }
              ],
              "projections": [],
              "cards": [
                {
                  "reference": { "projectId": "p1", "cardId": "T-1" },
                  "kind": "Task",
                  "title": "Do the thing",
                  "workflowId": "task",
                  "stageId": "backlog",
                  "revision": 2,
                  "ownPriority": 1.5,
                  "declaredScopeFiles": [ "src/app.cs" ],
                  "actualChangedFiles": [],
                  "metadata": {},
                  "origin": null,
                  "criterionValues": { "complexity": 2.0 }
                }
              ],
              "relations": [],
              "cardPriorities": []
            }
            """;

        var snapshot = JsonSerializer.Deserialize<ProjectBoardSnapshot>(payload, PwaJson.Options);

        Assert.NotNull(snapshot);
        Assert.Equal("p1", snapshot!.Project.Id);
        Assert.Equal(CardKind.Task, snapshot.Cards[0].Kind);
        Assert.Equal("T-1", snapshot.Cards[0].Reference.CardId);
        Assert.Equal(3, snapshot.Workflows[0].Revision);
        Assert.Equal(2.0m, snapshot.Cards[0].CriterionValues!["complexity"]);
    }

    [Fact]
    public void Project_event_of_the_sse_stream_deserializes()
    {
        const string payload =
            """{"id":7,"projectId":"p1","type":"card.updated","occurredAtUtc":"2026-01-01T00:00:00+00:00","payloadJson":"{}"}""";

        var parsed = JsonSerializer.Deserialize<AikoEvent>(payload, PwaJson.Options);

        Assert.NotNull(parsed);
        Assert.Equal(7, parsed!.Id);
        Assert.Equal(AikoEventTypes.CardUpdated, parsed.Type);
    }
}
