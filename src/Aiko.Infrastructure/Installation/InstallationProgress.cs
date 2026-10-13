namespace Aiko.Infrastructure.Installation;

/// <summary>
/// What an installation run tells the caller that is waiting for it.
/// </summary>
/// <remarks>
/// Called <b>before</b> the work of a phase, not after it: the point is to say what is being waited for while
/// it is being waited for, because the longest part of a run - a download - says nothing on its own. The
/// report an <see cref="InstallationEngine"/> returns stays the record of what happened, in order, and is what
/// a script reads; this is what a person watching sees (TASK-352).
/// </remarks>
public interface IInstallationProgress
{
    /// <summary>
    /// Names the phase that is about to run.
    /// </summary>
    /// <param name="phase">
    /// Stable phase name, the one the report uses as well: <c>resolve</c>, <c>stage</c>, <c>download</c>,
    /// <c>verify</c>, <c>unpack</c>, <c>daemon</c>, <c>replace</c>, <c>repair</c>, <c>doctor</c>.
    /// </param>
    /// <param name="detail">What is about to happen, or what just finished, in one line.</param>
    void Phase(string phase, string detail);
}
