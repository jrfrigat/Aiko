using System.Threading.Channels;
using Aiko.Application.Contracts;

namespace Aiko.Application.Contracts;

/// <summary>
/// A live subscription to the events of one project. Dispose to stop receiving.
/// </summary>
public interface IAikoEventSubscription : IDisposable
{
    /// <summary>
    /// Reader receiving the events published for the subscribed project.
    /// </summary>
    ChannelReader<AikoEvent> Events { get; }
}
