using System.Collections.Concurrent;

namespace RCDragLiveServer.Services;

public sealed class InMemoryDialInRateLimiter : IDialInRateLimiter
{
    private readonly ConcurrentDictionary<string, DateTime> _lastAccepted = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(5);

    public bool TryAcquire(string eventId, int driverId)
    {
        if (string.IsNullOrWhiteSpace(eventId) || driverId <= 0)
            return false;

        var key = eventId + "|" + driverId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var now = DateTime.UtcNow;
        if (_lastAccepted.TryGetValue(key, out var last) && now - last < Cooldown)
            return false;
        _lastAccepted[key] = now;
        return true;
    }
}
