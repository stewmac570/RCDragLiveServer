using RCDragLiveServer.Models;

namespace RCDragLiveServer.Services;

public class InMemoryLiveRaceStateStore : ILiveRaceStateStore
{
    private static readonly TimeSpan EventExpiry = TimeSpan.FromHours(2);

    private sealed class EventBucket
    {
        public Dictionary<string, LiveRaceState> Classes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }

    private readonly object _sync = new();
    private readonly Dictionary<string, EventBucket> _events = new(StringComparer.OrdinalIgnoreCase);
    private readonly IDialInStore _dialInStore;

    public InMemoryLiveRaceStateStore(IDialInStore dialInStore)
    {
        _dialInStore = dialInStore;
    }

    public LiveRaceState GetLatest()
    {
        lock (_sync)
        {
            PurgeExpired();
            if (_events.Count == 0) return new LiveRaceState();

            // Return latest state from the most recently updated event
            var bucket = _events.Values.OrderByDescending(b => b.LastUpdated).First();
            return bucket.Classes.Values.OrderBy(s => s.ClassType, StringComparer.OrdinalIgnoreCase).First();
        }
    }

    // Returns all classes across all active events (flat), for backward-compat with /api/live
    public Dictionary<string, LiveRaceState> GetAll()
    {
        lock (_sync)
        {
            PurgeExpired();

            // If only one event active, return its classes keyed by classType (existing behaviour)
            if (_events.Count == 1)
                return new Dictionary<string, LiveRaceState>(_events.Values.First().Classes, StringComparer.OrdinalIgnoreCase);

            // Multiple events: key by "eventId/classType" to avoid collisions
            var result = new Dictionary<string, LiveRaceState>(StringComparer.OrdinalIgnoreCase);
            foreach (var (eventId, bucket) in _events)
            {
                foreach (var (classType, state) in bucket.Classes)
                {
                    result[eventId + "/" + classType] = state;
                }
            }
            return result;
        }
    }

    public void Upsert(LiveRaceState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        lock (_sync)
        {
            PurgeExpired();

            // Determine the event key: prefer EventId, fall back to EventName for old clients
            string eventKey = !string.IsNullOrWhiteSpace(state.EventId)
                ? state.EventId
                : (!string.IsNullOrWhiteSpace(state.EventName) ? state.EventName : "(default)");

            if (!_events.TryGetValue(eventKey, out var bucket))
            {
                bucket = new EventBucket();
                _events[eventKey] = bucket;
            }

            _dialInStore.SetLocked(state.DialInLocked);

            string classKey = string.IsNullOrWhiteSpace(state.ClassType) ? "(Unknown)" : state.ClassType;
            bucket.Classes[classKey] = state;
            bucket.LastUpdated = DateTime.UtcNow;
        }
    }

    public IReadOnlyList<EventSummary> GetActiveEvents()
    {
        lock (_sync)
        {
            PurgeExpired();

            return _events
                .Select(kvp =>
                {
                    var firstClass = kvp.Value.Classes.Values.FirstOrDefault();
                    return new EventSummary(
                        EventId: kvp.Key,
                        EventName: firstClass?.EventName ?? kvp.Key,
                        EventDate: firstClass?.EventDate ?? string.Empty,
                        ClassCount: kvp.Value.Classes.Count,
                        LastUpdated: kvp.Value.LastUpdated);
                })
                .OrderByDescending(s => s.LastUpdated)
                .ToList();
        }
    }

    public Dictionary<string, LiveRaceState>? GetEvent(string eventId)
    {
        lock (_sync)
        {
            PurgeExpired();

            if (!_events.TryGetValue(eventId, out var bucket))
                return null;

            return new Dictionary<string, LiveRaceState>(bucket.Classes, StringComparer.OrdinalIgnoreCase);
        }
    }

    private void PurgeExpired()
    {
        var cutoff = DateTime.UtcNow - EventExpiry;
        var expired = _events
            .Where(kvp => kvp.Value.LastUpdated < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expired)
            _events.Remove(key);
    }
}
