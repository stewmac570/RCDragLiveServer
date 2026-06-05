using RCDragLiveServer.Models;

namespace RCDragLiveServer.Services;

public interface ILiveRaceStateStore
{
    LiveRaceState GetLatest();
    Dictionary<string, LiveRaceState> GetAll();
    void Upsert(LiveRaceState state);
    void ClearEvent(string eventId, string? eventName);
    IReadOnlyList<EventSummary> GetActiveEvents();
    Dictionary<string, LiveRaceState>? GetEvent(string eventId);
    string ResolveEventKey(string eventId);
    bool EventHasDriver(string eventId, int driverId);
}
