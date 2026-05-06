namespace RCDragLiveServer.Services;

public sealed class InMemoryDialInStore : IDialInStore
{
    private sealed class EventData
    {
        public Dictionary<string, string> Pins { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, double?> DialIns { get; } = new(StringComparer.OrdinalIgnoreCase);
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

    public (bool success, string? error) SubmitUpdate(string eventId, string driverName, double? dialIn, string? pin)
    {
        if (string.IsNullOrWhiteSpace(eventId))
            return (false, "invalid_event");
        if (string.IsNullOrWhiteSpace(driverName))
            return (false, "invalid_driver");

        lock (_sync)
        {
            var data = GetOrCreate(eventId);

            if (data.Locked)
                return (false, "locked");

            if (data.Pins.TryGetValue(driverName, out var existingPin))
            {
                if (string.IsNullOrEmpty(pin) || pin != existingPin)
                    return (false, "invalid_pin");
            }
            else if (!string.IsNullOrEmpty(pin))
            {
                if (pin.Length != 4 || !pin.All(char.IsDigit))
                    return (false, "invalid_pin_format");
                data.Pins[driverName] = pin;
            }

            data.DialIns[driverName] = dialIn;
            return (true, null);
        }
    }

    public Dictionary<string, double?> GetAll(string eventId)
    {
        lock (_sync)
        {
            if (!_events.TryGetValue(eventId, out var data))
                return new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
            return new Dictionary<string, double?>(data.DialIns, StringComparer.OrdinalIgnoreCase);
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
