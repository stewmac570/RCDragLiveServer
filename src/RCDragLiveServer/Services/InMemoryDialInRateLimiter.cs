using System.Collections.Concurrent;

namespace RCDragLiveServer.Services;

public sealed class InMemoryDialInRateLimiter : IDialInRateLimiter
{
    private readonly ConcurrentDictionary<int, DateTime> _lastAccepted = new();
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(5);

    public bool TryAcquire(int driverId)
    {
        var now = DateTime.UtcNow;
        if (_lastAccepted.TryGetValue(driverId, out var last) && now - last < Cooldown)
            return false;
        _lastAccepted[driverId] = now;
        return true;
    }
}
