namespace Aiko.Application.Installation;

/// <summary>
/// One step an installation run went through, as data rather than as a line of console output.
/// </summary>
/// <remarks>
/// The same steps are shown by the exe and by the interface, so a step that existed only as console text would
/// have to be rewritten as data for the second caller. The name is stable and untranslated - <c>resolve</c>,
/// <c>replace</c>, and the like - and the detail is what a person reads.
/// </remarks>
public sealed record InstallationStep(string Name, bool Succeeded, string Detail);
