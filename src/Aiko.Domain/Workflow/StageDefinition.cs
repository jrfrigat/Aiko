using Aiko.Domain.Cards;

namespace Aiko.Domain.Workflow;

/// <summary>
/// Definition of a pipeline stage: agent instruction, allowed card kinds,
/// required artifacts and action policies.
/// </summary>
public sealed record StageDefinition(
    string Id,
    string Title,
    int Order,
    string Instruction,
    IReadOnlyList<CardKind> AllowedCardKinds,
    string? DefaultAgentAdapterId,
    IReadOnlyList<ArtifactRequirement> RequiredArtifacts,
    IReadOnlyDictionary<string, ActionPolicy> ActionPolicies);
