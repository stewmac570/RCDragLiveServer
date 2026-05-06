namespace RCDragLiveServer.Services;

public sealed class InMemoryDialInStore : IDialInStore
{
    private sealed class EventData
    {
        public Dictionary<int, string> Pins { get; } = new();
        public Dictionary<int, double?> DialIns { get; } = new();
        public bool Locked { get; set; }
    }

    private readonly object _sync = new();
    private readonly Dictionary<string, EventData> _events = new(StringComparer.OrdinalIgnoreCase);

    private EventData GetOrCreate(string eventId)
    {
        if (!_events.TryGetValue(eventId, out var data))
        {
            data = new EventData();
            _events[eventId] = data;
        }
        return data;
    }

    public bool IsLocked(string eventId)
    {
        lock (_sync)
        {
            return _events.TryGetValue(eventId, out var data) && data.Locked;
        }
    }

    public (bool success, string? error) SubmitUpdate(string eventId, int driverId, double? dialIn, string? pin)
    {
        if (string.IsNullOrWhiteSpace(eventId))
            return (false, "invalid_event");
        if (driverId <= 0)
            return (false, "invalid_driver");

        lock (_sync)
        {
            var data = GetOrCreate(eventId);

            if (data.Locked)
                return (false, "locked");

            if (data.Pins.TryGetValue(driverId, out var existingPin))
            {
                if (string.IsNullOrEmpty(pin) || pin != existingPin)
                    return (false, "invalid_pin");
            }
            else if (!string.IsNullOrEmpty(pin))
            {
                if (pin.Length != 4 || !pin.All(char.IsDigit))
                    return (false, "invalid_pin_format");
                data.Pins[driverId] = pin;
            }

            data.DialIns[driverId] = dialIn;
            return (true, null);
        }
    }

    public Dictionary<int, double?> GetAll(string eventId)
    {
        lock (_sync)
        {
            if (!_events.TryGetValue(eventId, out var data))
                return new Dictionary<int, double?>();
            return new Dictionary<int, double?>(data.DialIns);
        }
    }

    public void SetLocked(string eventId, bool locked)
    {
        lock (_sync)
        {
            GetOrCreate(eventId).Locked = locked;
        }
    }

    public void ClearAll(string eventId)
    {
        lock (_sync)
        {
            _events.Remove(eventId);
        }
    }
}
