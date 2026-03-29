using RCDragLiveServer.Models;

namespace RCDragLiveServer.Services;

public interface ILiveRaceStateStore
{
    LiveRaceState GetLatest();
    Dictionary<string, LiveRaceState> GetAll();
    void Upsert(LiveRaceState state);
}
