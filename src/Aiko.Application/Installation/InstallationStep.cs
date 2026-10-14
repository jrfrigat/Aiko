namespace Aiko.Application.Installation;

/// <summary>
/// One step an installation run went through, as data rather than as a line of console output.
/// </summary>
/// <remarks>
/// The same steps are shown by the exe and by the interface (the install TUI of the sibling issue), so a
/// step that existed only as text written to the console would have to be rewritten as data for the second
/// caller. The name is stable and untranslated; the detail is what a person reads.
/// </remarks>
/// <param name="Name">Stable step name, for example <c>resolve</c> or <c>replace</c>.</param>
/// <param name="Succeeded">Whether the step finished.</param>
/// <param name="Detail">Human-readable detail: what happened, or why it stopped.</param>
public sealed record InstallationStep(string Name, bool Succeeded, string Detail);
