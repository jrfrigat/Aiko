using System.Threading.Channels;
using Aiko.Application.Contracts;

namespace Aiko.Infrastructure.Events;

/// <summary>
/// In-process fan-out of published events to live subscribers, per project. Each subscriber
/// receives events through a bounded channel; when a slow subscriber overflows it, its stream is
/// ended so the client reconnects with Last-Event-Id and replays the missed events.
/// </summary>
public sealed class AikoEventBroadcaster
{
    private const int SubscriberCapacity = 1024;

    private readonly object _sync = new();
    private readonly List<Subscription> _subscriptions = [];

    /// <summary>
    /// Subscribes to the live events of a project. Dispose the subscription to stop receiving.
    /// </summary>
    public IAikoEventSubscription Subscribe(string projectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        var channel = Channel.CreateBounded<AikoEvent>(
            new BoundedChannelOptions(SubscriberCapacity)
            {
                SingleReader = true,
                FullMode = BoundedChannelFullMode.DropWrite
            });
        var subscription = new Subscription(this, projectId, channel);
        lock (_sync)
        {
            _subscriptions.Add(subscription);
        }

        return subscription;
    }

    /// <summary>
    /// Delivers a published event to every subscriber of its project.
    /// </summary>
    public void Broadcast(AikoEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        List<Subscription>? stalled = null;
        lock (_sync)
        {
            foreach (var subscription in _subscriptions)
            {
                if (!string.Equals(subscription.ProjectId, @event.ProjectId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!subscription.Channel.Writer.TryWrite(@event))
                {
                    // The subscriber fell behind; end its stream so the client reconnects
                    // and replays the missed events from the journal.
                    subscription.Channel.Writer.TryComplete();
                    (stalled ??= []).Add(subscription);
                }
            }
        }

        if (stalled is not null)
        {
            lock (_sync)
            {
                foreach (var subscription in stalled)
                {
                    _subscriptions.Remove(subscription);
                }
            }
        }
    }

    private void Remove(Subscription subscription)
    {
        lock (_sync)
        {
            _subscriptions.Remove(subscription);
        }
    }

    private sealed class Subscription(
        AikoEventBroadcaster owner,
        string projectId,
        Channel<AikoEvent> channel) : IAikoEventSubscription
    {
        public string ProjectId { get; } = projectId;

        public Channel<AikoEvent> Channel { get; } = channel;

        public ChannelReader<AikoEvent> Events => Channel.Reader;

        public void Dispose()
        {
            Channel.Writer.TryComplete();
            owner.Remove(this);
        }
    }
}
