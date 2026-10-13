namespace Aiko.Pwa.Services;

/// <summary>
/// Whether what a screen is showing belongs to the level it was asked about.
/// </summary>
/// <remarks>
/// A screen that reads its data per level has two different things to remember: the level it started reading
/// for, and the level whose values it is showing. The first is remembered before the request goes out - it is
/// what keeps a parent's render from reading again and throwing unsaved edits away - and it is not evidence
/// that anything came back. The second is set only when the values are in hand, and it is the only thing that
/// allows the form on the screen. Two different things, so two fields, and one place that decides which of
/// them is on screen (TASK-334 for the rule, TASK-348 for it being a rule a spec can call).
/// </remarks>
public static class ReadGate
{
    /// <summary>
    /// Whether the values on screen were read for the level being asked about. False while a read is in
    /// flight, after a level has been switched to, and for a level nothing has been asked for yet.
    /// </summary>
    /// <param name="loadedKey">The level a read was started for, or null when none has been.</param>
    /// <param name="appliedKey">The level whose values are on the screen, or null when none are.</param>
    public static bool Ready(string? loadedKey, string? appliedKey) =>
        !string.IsNullOrEmpty(loadedKey) && string.Equals(loadedKey, appliedKey, StringComparison.Ordinal);
}
