using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// Guards the card's command panel against the defect review found in TASK-70.
/// </summary>
/// <remarks>
/// The bug this pins: the panel was wired to the open half of the queue, so a command an agent had finished
/// vanished from the card - with the message saying what it did or why it could not. A request that failed
/// then looked exactly like one that was never placed, which is the opposite of what the card asks for
/// ("the execution and the error are reflected on the card"). Both halves are checked here: the client has to
/// read the whole queue, and the markup has to draw a closed command and its message.
/// </remarks>
public sealed class CommandQueueUiSpecs
{
    [Fact]
    public void The_client_reads_the_whole_queue_and_not_only_its_open_half()
    {
        var text = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "Aiko.Pwa", "Services", "WorkspaceState.cs"));

        Assert.Contains("commands?state=all", text, StringComparison.Ordinal);
        Assert.DoesNotContain("commands?state=open", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_card_draws_a_closed_command_with_what_the_agent_reported()
    {
        var text = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "Aiko.Pwa", "Pages", "CardInspector.razor"));

        // The list is the card's own commands, open ones first - the queue a person is watching - and then
        // what already happened.
        Assert.Contains("private IReadOnlyList<CardCommand> CardCommands", text, StringComparison.Ordinal);
        Assert.Contains("command.IsOpen", text, StringComparison.Ordinal);

        // Every state a command can be in has words, including the finished ones.
        foreach (var key in new[]
                 {
                     "CommandWaiting", "CommandTaken", "CommandDone", "CommandRefused", "CommandWithdrawn"
                 })
        {
            Assert.Contains($"Loc.Get(\"{key}\")", text, StringComparison.Ordinal);
        }

        // ...and the message the agent left is drawn, which is the half that was missing.
        Assert.Contains("entry.Message", text, StringComparison.Ordinal);
    }

    /// <summary>Walks up from this assembly to the solution file, as the other specs do.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(CommandQueueUiSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
