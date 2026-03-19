using RCDragLiveServer.Models;

namespace RCDragLiveServer.Services;

public class InMemoryLiveRaceStateStore : ILiveRaceStateStore
{
    private readonly object _sync = new();
    private LiveRaceState _latest = new();

    public LiveRaceState GetLatest()
    {
        lock (_sync)
        {
            return _latest;
        }
    }

    public void SetLatest(LiveRaceState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        lock (_sync)
        {
            _latest = state;
        }
    }
}
