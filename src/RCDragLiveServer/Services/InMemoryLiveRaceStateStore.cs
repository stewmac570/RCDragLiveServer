using RCDragLiveServer.Models;

namespace RCDragLiveServer.Services;

public class InMemoryLiveRaceStateStore : ILiveRaceStateStore
{
    private readonly object _sync = new();
    private readonly Dictionary<string, LiveRaceState> _classes = new(StringComparer.OrdinalIgnoreCase);
    private readonly IDialInStore _dialInStore;

    public InMemoryLiveRaceStateStore(IDialInStore dialInStore)
    {
        _dialInStore = dialInStore;
    }

    public LiveRaceState GetLatest()
    {
        lock (_sync)
        {
            if (_classes.Count == 0) return new LiveRaceState();
            string key = _classes.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).First();
            return _classes[key];
        }
    }

    public Dictionary<string, LiveRaceState> GetAll()
    {
        lock (_sync)
        {
            return new Dictionary<string, LiveRaceState>(_classes, StringComparer.OrdinalIgnoreCase);
        }
    }

    public void Upsert(LiveRaceState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        lock (_sync)
        {
            if (_classes.Count > 0)
            {
                string existingEventName = _classes.Values.First().EventName;
                if (!string.Equals(existingEventName, state.EventName, StringComparison.OrdinalIgnoreCase))
                {
                    _classes.Clear();
                    _dialInStore.ClearAll();
                }
            }

            _dialInStore.SetLocked(state.DialInLocked);

            string key = string.IsNullOrWhiteSpace(state.ClassType) ? "(Unknown)" : state.ClassType;
            _classes[key] = state;
        }
    }
}
