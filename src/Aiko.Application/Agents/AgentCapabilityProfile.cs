namespace Aiko.Application.Agents;

/// <summary>
/// What an agent can do, as declared in its own configuration rather than inferred from its executable.
/// </summary>
/// <param name="AikoAccess">How the agent reaches Aiko: <c>mcp</c>, <c>rest</c> or <c>cli</c>.</param>
/// <param name="Actions">Command actions the agent carries out; <c>*</c> means every action.</param>
/// <param name="Stages">Stages the agent may run; <c>*</c> means every stage.</param>
/// <param name="CardTypes">Card types the agent may work; <c>*</c> means every type.</param>
/// <param name="PermissionModes">Permission modes Aiko may grant the agent.</param>
/// <param name="MaxConcurrentSessions">How many direct sessions the agent may run at once.</param>
/// <param name="Resume">Whether an interrupted session can be resumed.</param>
/// <param name="StructuredOutput">Whether the agent returns structured results.</param>
public sealed record AgentCapabilityProfile(
    IReadOnlyList<string>? AikoAccess = null,
    IReadOnlyList<string>? Actions = null,
    IReadOnlyList<string>? Stages = null,
    IReadOnlyList<string>? CardTypes = null,
    IReadOnlyList<string>? PermissionModes = null,
    int? MaxConcurrentSessions = null,
    bool? Resume = null,
    bool? StructuredOutput = null)
{
    /// <summary>The capabilities an adapter gets when it states none of its own.</summary>
    public static AgentCapabilityProfile Default { get; } = new(
        AikoAccess: ["mcp"],
        Actions: ["start", "pause", "resume", "answer"],
        Stages: ["*"],
        CardTypes: ["*"],
        PermissionModes: ["read-only", "workspace-write"],
        MaxConcurrentSessions: 1,
        Resume: true,
        StructuredOutput: true);
}
