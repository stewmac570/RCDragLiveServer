namespace RCDragLiveServer.Services;

/// <summary>
/// Tells connected browsers that an event's live state has moved on, so pages can
/// update instead of reloading themselves on a timer.
///
/// Deliberately carries no payload: a subscriber that wakes re-reads the current
/// state itself. That keeps rendering in one place and means a burst of pushes
/// from the desktop (a winner submitted, the round advanced, the bracket redrawn)
/// costs one refresh, not three.
/// </summary>
public interface ILiveUpdateBroadcaster
{
    /// <summary>Opens a subscription for one event. Dispose to unsubscribe.</summary>
    ILiveUpdateSubscription Subscribe(string eventKey);

    /// <summary>Wakes every browser watching this event.</summary>
    void Publish(string eventKey);

    /// <summary>Live subscriber count, for diagnostics.</summary>
    int SubscriberCount { get; }
}

public interface ILiveUpdateSubscription : IDisposable
{
    /// <summary>Completes when the event changes, or when the token is cancelled.
    /// Returns false if the subscription is finished.</summary>
    Task<bool> WaitForChangeAsync(CancellationToken cancellationToken);
}
