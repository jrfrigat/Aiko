namespace Aiko.Application.Contracts;

/// <summary>
/// A stage execution that is running right now, with the file scope its card declared.
/// </summary>
/// <remarks>
/// The work queue reads these to answer "what could run beside what" with the same facts the start gate
/// weighs, rather than re-deriving the rule on its own and drifting from it. Only a running execution is here:
/// a paused or waiting run is not writing files, so it is not a conflict (TASK-89).
/// </remarks>
/// <param name="CardId">The card whose stage is running.</param>
/// <param name="DeclaredScopeFiles">The file patterns that run declared in its card.</param>
public sealed record RunningScope(
    string CardId,
    IReadOnlyList<string> DeclaredScopeFiles);
