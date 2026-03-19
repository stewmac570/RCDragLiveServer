using RCDragLiveServer.Models;

namespace RCDragLiveServer.Services;

public interface ILiveRaceStateStore
{
    LiveRaceState GetLatest();
    void SetLatest(LiveRaceState state);
}
