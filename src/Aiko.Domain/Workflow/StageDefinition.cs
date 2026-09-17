using Aiko.Domain.Cards;

namespace Aiko.Domain.Workflow;

/// <summary>
/// Definition of a pipeline stage: agent instruction, allowed card kinds and executors,
/// required artifacts, action policies and verification commands.
/// </summary>
/// <param name="Id">Stable ASCII identifier, for example <c>implementation</c>.</param>
/// <param name="Title">Display name of the stage.</param>
/// <param name="Order">Position in the pipeline; lower runs earlier.</param>
/// <param name="Instruction">What the agent is asked to do at this stage.</param>
/// <param name="AllowedCardKinds">Card type ids this stage accepts.</param>
/// <param name="DefaultAgentAdapterId">Adapter the stage starts with, or null to leave the choice open.</param>
/// <param name="RequiredArtifacts">Artifacts the pipeline expects from this stage.</param>
/// <param name="ActionPolicies">Policies for dangerous actions at this stage, keyed by action name.</param>
/// <param name="AllowedAgentAdapterIds">
/// Adapters allowed to run this stage (ТЗ §11). Null or empty means every discovered adapter is allowed;
/// a non-empty list restricts the stage, and the default adapter must be one of them.
/// </param>
/// <param name="ValidationCommands">
/// Commands that verify this stage's outcome (ТЗ §11), for example <c>dotnet test --no-build</c>. They are
/// recorded and shown to the agent as the stage's own definition of done; Aiko does not run them itself.
/// </param>
/// <param name="SkillsBeforeInstruction">
/// Skills the agent should invoke before it reads the instruction, for example a context or search skill
/// that gathers what the instruction then reasons about. Names only: Aiko records them and hands them to the
/// agent, it does not resolve or run them.
/// </param>
/// <param name="SkillsAfterInstruction">
/// Skills the agent should invoke once the instruction is done - reporting, verification or handoff. Kept
/// apart from <paramref name="SkillsBeforeInstruction"/> because the order is the point: a skill that
/// gathers input and a skill that publishes output are not interchangeable.
/// </param>
/// <param name="Icon">
/// Which icon the status column draws, as an id from <see cref="AppearanceCatalog.Icons"/>, or null to keep
/// the positional default the board falls back to. Null keeps every workflow written before the choice
/// existed readable, which is why it is optional.
/// </param>
/// <param name="Color">
/// Which accent colour the status column draws, as an id from <see cref="AppearanceCatalog.Colors"/>, or
/// null for the positional default.
/// </param>
public sealed record StageDefinition(
    string Id,
    string Title,
    int Order,
    string Instruction,
    IReadOnlyList<string> AllowedCardKinds,
    string? DefaultAgentAdapterId,
    IReadOnlyList<ArtifactRequirement> RequiredArtifacts,
    IReadOnlyDictionary<string, ActionPolicy> ActionPolicies,
    IReadOnlyList<string>? AllowedAgentAdapterIds = null,
    IReadOnlyList<string>? ValidationCommands = null,
    IReadOnlyList<string>? SkillsBeforeInstruction = null,
    IReadOnlyList<string>? SkillsAfterInstruction = null,
    string? Icon = null,
    string? Color = null)
{
    /// <summary>The allowed executors, or an empty list when the stage allows every adapter.</summary>
    public IReadOnlyList<string> AllowedAgents => AllowedAgentAdapterIds ?? [];

    /// <summary>The verification commands of the stage, or an empty list.</summary>
    public IReadOnlyList<string> Commands => ValidationCommands ?? [];

    /// <summary>The skills the agent invokes before the instruction, or an empty list.</summary>
    public IReadOnlyList<string> BeforeSkills => SkillsBeforeInstruction ?? [];

    /// <summary>The skills the agent invokes after the instruction, or an empty list.</summary>
    public IReadOnlyList<string> AfterSkills => SkillsAfterInstruction ?? [];

    /// <summary>
    /// Whether an adapter may run this stage. The default adapter of the stage is always allowed, so a
    /// stage cannot be configured into a state where nothing can start it.
    /// </summary>
    /// <param name="agentAdapterId">Adapter to test, or null for "any".</param>
    public bool Allows(string? agentAdapterId)
    {
        if (string.IsNullOrWhiteSpace(agentAdapterId))
        {
            return true;
        }

        if (StringComparer.Ordinal.Equals(agentAdapterId, DefaultAgentAdapterId))
        {
            return true;
        }

        return AllowedAgents.Count == 0 ||
               AllowedAgents.Contains(agentAdapterId, StringComparer.Ordinal);
    }
}
