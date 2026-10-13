using System.Globalization;
using Aiko.Server.Security;
using Xunit;

namespace Aiko.Mcp.Specs;

/// <summary>
/// The window a pairing code is usable in. A person reads the code off the daemon's console and pastes it, so
/// the window is minutes - and it closes by itself, so a code that was left on a screen does not pair for ever
/// (TASK-159, and this card for the rule itself): a code past its window is refused, and one whose window has
/// closed is dropped rather than kept to be found later.
/// </summary>
/// <remarks>
/// The rule is read in the type that owns it, not through the daemon's routes: the routes can say only
/// "refused", which is the same answer for an expired code and an invented one. The service already takes a
/// <see cref="TimeProvider"/>, so the clock is moved instead of waiting five minutes - and moving it is what
/// makes the second fact below possible at all.
/// </remarks>
public sealed class PairingExpirySpecs
{
    [Fact]
    public void A_code_is_honoured_inside_its_window_and_refused_after_it()
    {
        var clock = new Clock();
        var pairing = new PairingService(clock);

        // The positive control first: a window that refused everything would pass the other half of this fact
        // and say nothing about the rule.
        var pasted = pairing.GenerateCode();
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(pairing.TryConsume(pasted), "A code pasted a minute later should pair.");

        var forgotten = pairing.GenerateCode();
        clock.Advance(TimeSpan.FromMinutes(6));
        Assert.False(pairing.TryConsume(forgotten), "A code pasted six minutes later should not pair.");
    }

    [Fact]
    public void A_code_whose_window_closed_is_dropped_rather_than_left_to_be_found_later()
    {
        var clock = new Clock();
        var pairing = new PairingService(clock);

        var expired = pairing.GenerateCode();
        clock.Advance(TimeSpan.FromMinutes(6));
        // Any later use of the service sweeps the expired entries, which is what a daemon that keeps issuing
        // codes does on its own.
        var issued = pairing.GenerateCode();

        // The one way to tell "dropped" from "still there, merely refused": a code that was only ignored would
        // be valid again at a time inside its window, and stepping the clock back is how a person gets there.
        // The code that was never expired is checked too - the step back must not have swept it instead.
        clock.Advance(TimeSpan.FromMinutes(-6));
        Assert.False(
            pairing.TryConsume(expired),
            "The expired code came back to life when the clock stepped back inside its window.");
        Assert.True(pairing.TryConsume(issued), "The code issued later should still pair.");
    }

    /// <summary>System time, moved by hand: the rule is about minutes and a spec has none to spare.</summary>
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.Parse("2026-10-13T12:00:00Z", CultureInfo.InvariantCulture);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
