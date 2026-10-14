namespace Aiko.Application.Contracts;

/// <summary>
/// A stage execution that is running right now, with the file scope its card declared.
/// </summary>
/// <remarks>
/// Only running executions are here: a paused or waiting run writes no files, so it is no conflict.
/// </remarks>
public sealed record RunningScope(
    string CardId,
    IReadOnlyList<string> DeclaredScopeFiles);
