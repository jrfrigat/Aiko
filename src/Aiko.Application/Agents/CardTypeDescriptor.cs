namespace Aiko.Application.Agents;

/// <summary>
/// One card type a project defines, in the shape the agent adapters need it.
/// </summary>
/// <remarks>
/// A card type is the workflow of the same name, so this is a projection of the project's pipelines rather
/// than a second definition of them. It exists because the adapters are pure functions of their arguments:
/// they write files without reading the project, so the daemon hands them the types it found.
/// </remarks>
/// <param name="Id">Workflow id, which is also the card type id (for example <c>bug</c>).</param>
/// <param name="Title">Display name of the type.</param>
/// <param name="Description">What the type is for, in the project's words, or null.</param>
public sealed record CardTypeDescriptor(string Id, string Title, string? Description);
