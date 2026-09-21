using Aiko.Application.Contracts;
using Aiko.Domain.Execution;
using Aiko.Pwa.Services;
using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// Guards the rule behind the board's answer filter: a card is waiting for a person when its latest run waits
/// for one and the last word in its feed is an agent's. Both halves matter - the run says the agent stopped to
/// ask, the feed says whose turn it is - and either half alone would select cards nobody is waiting on.
/// </summary>
public sealed class BoardWaitingSpecs
{
    [Fact]
    public void A_card_waits_only_while_the_agent_has_the_last_word()
    {
        // The agent asked and nobody has answered: this is the card the filter is for.
        Assert.True(BoardWaiting.AwaitsAnswer(
            nameof(StageExecutionState.WaitingForUser),
            [Comment("cline", 1)]));

        // The person answered: the same card leaves the selection without any run having changed.
        Assert.False(BoardWaiting.AwaitsAnswer(
            nameof(StageExecutionState.WaitingForUser),
            [Comment("cline", 1), Comment(string.Empty, 2)]));
    }

    [Theory]
    [InlineData("Running")]
    [InlineData("Paused")]
    [InlineData("NeedsAttention")]
    [InlineData("Completed")]
    [InlineData("")]
    [InlineData(null)]
    public void A_run_that_is_not_waiting_leaves_the_card_out(string? state) =>
        Assert.False(BoardWaiting.AwaitsAnswer(state, [Comment("cline", 1)]));

    [Fact]
    public void The_state_is_read_however_it_is_spelled()
    {
        // The board carries the state as text, and the comparison is the one the active panel already makes.
        Assert.True(BoardWaiting.IsWaitingForUser("waitingforuser"));
        Assert.True(BoardWaiting.AwaitsAnswer("WAITINGFORUSER", [Comment("cline", 1)]));
    }

    [Fact]
    public void A_card_whose_feed_says_nothing_from_an_agent_does_not_wait()
    {
        var state = nameof(StageExecutionState.WaitingForUser);

        // Nobody was asked in the feed: a run waiting for a user is not the same thing as a question on the
        // screen, and the filter is about the question.
        Assert.False(BoardWaiting.AwaitsAnswer(state, null));
        Assert.False(BoardWaiting.AwaitsAnswer(state, []));

        // Only the person has spoken, and a blank author is the local user - never an agent.
        Assert.False(BoardWaiting.AwaitsAnswer(state, [Comment(string.Empty, 1)]));
        Assert.False(BoardWaiting.AwaitsAnswer(state, [Comment("   ", 1)]));
    }

    [Fact]
    public void The_last_note_is_decided_by_time_not_by_the_order_of_the_list()
    {
        var state = nameof(StageExecutionState.WaitingForUser);
        var person = Comment(string.Empty, 3);
        var agent = Comment("cline", 1);

        // The feed may arrive in any order; the newest note is the one that decides whose turn it is.
        Assert.False(BoardWaiting.AwaitsAnswer(state, [person, agent]));
        Assert.True(BoardWaiting.AwaitsAnswer(state, [agent, person with { CreatedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) }]));
    }

    [Fact]
    public void Notes_written_in_the_same_instant_keep_the_order_they_were_filed_in()
    {
        var state = nameof(StageExecutionState.WaitingForUser);
        var agent = Comment("cline", 5);
        var person = Comment(string.Empty, 5);

        // Equal timestamps are a real case in a feed appended twice in one second: the sort is stable, so the
        // note filed last is the last word.
        Assert.False(BoardWaiting.AwaitsAnswer(state, [agent, person]));
        Assert.True(BoardWaiting.AwaitsAnswer(state, [person, agent]));
    }

    private static CardComment Comment(string author, int minute) =>
        new(
            $"note-{minute}-{author.Length}",
            author,
            "body",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(minute));
}
