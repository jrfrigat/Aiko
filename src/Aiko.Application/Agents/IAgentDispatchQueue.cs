namespace Aiko.Application.Agents;

/// <summary>Wakes the dispatcher when a command is placed.</summary>
public interface IAgentDispatchQueue
{
    /// <summary>Asks the dispatcher to sweep the open queue.</summary>
    void Signal();

    /// <summary>Waits for the next signal.</summary>
    ValueTask WaitAsync(CancellationToken cancellationToken);
}
