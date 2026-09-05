using System.Collections.Concurrent;

namespace RCDragLiveServer.Services;

/// <summary>
/// In-process fan-out, which is all this server can support: race state already
/// lives in memory singletons, so it is effectively single-instance. If it is ever
/// scaled out, subscribers on one instance will not hear pushes that land on
/// another -- the same limitation the state store already has.
/// </summary>
public sealed class InMemoryLiveUpdateBroadcaster : ILiveUpdateBroadcaster
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Subscription, byte>> _subscribers =
        new(StringComparer.OrdinalIgnoreCase);

    private int _subscriberCount;

    public int SubscriberCount => Volatile.Read(ref _subscriberCount);

    public ILiveUpdateSubscription Subscribe(string eventKey)
    {
        var key = Normalise(eventKey);
        var subscription = new Subscription(this, key);

        var bucket = _subscribers.GetOrAdd(key, _ => new ConcurrentDictionary<Subscription, byte>());
        bucket[subscription] = 0;
        Interlocked.Increment(ref _subscriberCount);

        return subscription;
    }

    public void Publish(string eventKey)
    {
        if (!_subscribers.TryGetValue(Normalise(eventKey), out var bucket))
            return;

        foreach (var subscription in bucket.Keys)
            subscription.Signal();
    }

    private static string Normalise(string? eventKey) =>
        string.IsNullOrWhiteSpace(eventKey) ? "(default)" : eventKey;

    private void Remove(Subscription subscription, string eventKey)
    {
        if (_subscribers.TryGetValue(eventKey, out var bucket) && bucket.TryRemove(subscription, out _))
        {
            Interlocked.Decrement(ref _subscriberCount);

            // Keep the dictionary from growing a key per finished event.
            if (bucket.IsEmpty)
                _subscribers.TryRemove(eventKey, out _);
        }
    }

    private sealed class Subscription : ILiveUpdateSubscription
    {
        private readonly InMemoryLiveUpdateBroadcaster _owner;
        private readonly string _eventKey;

        // Reset on each wait, so several pushes arriving while a page is mid-refresh
        // collapse into the single refresh that follows.
        private readonly SemaphoreSlim _signal = new(0, 1);
        private bool _disposed;

        public Subscription(InMemoryLiveUpdateBroadcaster owner, string eventKey)
        {
            _owner = owner;
            _eventKey = eventKey;
        }

        public void Signal()
        {
            if (_disposed) return;

            // Release only when nobody has already been signalled; a waiting page
            // does not need to know how many pushes it missed.
            try
            {
                if (_signal.CurrentCount == 0)
                    _signal.Release();
            }
            catch (SemaphoreFullException)
            {
                // Raced with another Publish. One wake-up is enough.
            }
            catch (ObjectDisposedException)
            {
                // Subscriber went away mid-publish.
            }
        }

        public async Task<bool> WaitForChangeAsync(CancellationToken cancellationToken)
        {
            if (_disposed) return false;

            try
            {
                await _signal.WaitAsync(cancellationToken);
                return !_disposed;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _owner.Remove(this, _eventKey);
            _signal.Dispose();
        }
    }
}
