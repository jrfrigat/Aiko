namespace Aiko.Pwa.Services;

/// <summary>
/// Whether this tab is on screen, and which board read may still speak for it.
/// </summary>
/// <remarks>
/// Two rules come out of one browser behaviour, and both live here rather than in the reload paths that use
/// them. A hidden tab is throttled - its timers fire late and a request sent from it can be answered long
/// after the client's own deadline has passed, which is how a minimized window reported a failure for a
/// daemon that was answering. So: a tab that is not on screen does not start a board read at all, because
/// nobody would see the result; and a read that was in flight while the tab was away may not report its
/// failure, because it says nothing about the board the person is looking at now.
/// </remarks>
internal sealed class BoardPresence
{
    /// <summary>
    /// Whether the document is on screen, as the browser last reported it. A tab starts visible: only the
    /// browser can say otherwise, and without its word this is the behaviour that existed before.
    /// </summary>
    public bool Visible { get; private set; } = true;

    /// <summary>
    /// What the reads in flight belong to. A read takes this value before it starts and compares it when it
    /// fails: a different value means the tab has left the state that read was made for.
    /// </summary>
    public int Epoch { get; private set; }

    /// <summary>Whether a board read may start now.</summary>
    public bool CanRead => Visible;

    /// <summary>
    /// Applies what the browser reports and says whether anything changed, so a repeated report - the state
    /// read when the subscription is made, say - is not a reason to read the board again.
    /// </summary>
    /// <param name="visible">Whether the document is on screen.</param>
    /// <returns>True when the report changed the state.</returns>
    public bool Apply(bool visible)
    {
        if (visible == Visible)
        {
            return false;
        }

        Visible = visible;
        // Both directions end what is in flight: the state a read was started for is gone - either the
        // result would be shown to nobody, or the reading that follows is the one that has to be right.
        Epoch++;
        return true;
    }

    /// <summary>
    /// Ends what is in flight without a change of visibility: the link dropped and came back, so a failure
    /// that happened while it was gone is no longer the person's news.
    /// </summary>
    public void Abandon() => Epoch++;

    /// <summary>
    /// Whether a read that started at <paramref name="epoch"/> still speaks for this tab.
    /// </summary>
    /// <param name="epoch">The value <see cref="Epoch"/> had when the read started.</param>
    /// <returns>True when the read is still the tab's own.</returns>
    public bool IsCurrent(int epoch) => epoch == Epoch;
}
