namespace RCDragLiveServer.Services;

public sealed class InMemoryDialInStore : IDialInStore
{
    private sealed class EventData
    {
        // Stored as BCrypt hashes, never plaintext.
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
        if (!IsValidDialIn(dialIn))
            return (false, "invalid_dialin");

        // A PIN is mandatory: the first submission claims the driver id, and every
        // later change has to match it. Without one there is nothing separating
        // one driver's entry from anybody else's.
        if (string.IsNullOrEmpty(pin) || pin.Length != 4 || !pin.All(char.IsDigit))
            return (false, "invalid_pin_format");

        // Hash before acquiring the lock so BCrypt work doesn't block other
        // callers longer than necessary.
        var newHash = BCrypt.Net.BCrypt.HashPassword(pin);

        lock (_sync)
        {
            var data = GetOrCreate(eventId);

            if (data.Locked)
                return (false, "locked");

            if (data.Pins.TryGetValue(driverId, out var existingHash))
            {
                if (!BCrypt.Net.BCrypt.Verify(pin, existingHash))
                    return (false, "invalid_pin");
            }
            else
            {
                data.Pins[driverId] = newHash;
            }

            data.DialIns[driverId] = dialIn;
            return (true, null);
        }
    }

    private static bool IsValidDialIn(double? dialIn)
    {
        return dialIn.HasValue &&
            dialIn.Value > 0 &&
            !double.IsNaN(dialIn.Value) &&
            !double.IsInfinity(dialIn.Value);
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
