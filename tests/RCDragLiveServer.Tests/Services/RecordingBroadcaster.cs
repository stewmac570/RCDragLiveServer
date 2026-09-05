using RCDragLiveServer.Services;

namespace RCDragLiveServer.Tests.Services;

/// <summary>Records which event keys were woken, so tests can assert that a change
/// actually reaches the browsers rather than only reaching the store.</summary>
public sealed class RecordingBroadcaster : ILiveUpdateBroadcaster
{
    public List<string> Published { get; } = new();

    public int SubscriberCount => 0;

    public ILiveUpdateSubscription Subscribe(string eventKey) => new NoOpSubscription();

    public void Publish(string eventKey) => Published.Add(eventKey);

    private sealed class NoOpSubscription : ILiveUpdateSubscription
    {
        public Task<bool> WaitForChangeAsync(CancellationToken cancellationToken) => Task.FromResult(false);
        public void Dispose() { }
    }
}
