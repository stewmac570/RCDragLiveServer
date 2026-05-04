namespace RCDragLiveServer.Services;

public sealed class InMemoryDialInStore : IDialInStore
{
    private readonly object _sync = new();
    private readonly Dictionary<string, string> _pins = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double?> _dialIns = new(StringComparer.OrdinalIgnoreCase);
    private bool _locked;

    public bool IsLocked
    {
        get { lock (_sync) { return _locked; } }
    }

    public (bool success, string? error) SubmitUpdate(string driverName, double? dialIn, string? pin)
    {
        if (string.IsNullOrWhiteSpace(driverName))
            return (false, "invalid_driver");

        lock (_sync)
        {
            if (_locked)
                return (false, "locked");

            if (_pins.TryGetValue(driverName, out var existingPin))
            {
                // PIN is set — must match
                if (string.IsNullOrEmpty(pin) || pin != existingPin)
                    return (false, "invalid_pin");
            }
            else if (!string.IsNullOrEmpty(pin))
            {
                // First time setting a PIN — validate format (4 digits)
                if (pin.Length != 4 || !pin.All(char.IsDigit))
                    return (false, "invalid_pin_format");
                _pins[driverName] = pin;
            }

            _dialIns[driverName] = dialIn;
            return (true, null);
        }
    }

    public Dictionary<string, double?> GetAll()
    {
        lock (_sync)
        {
            return new Dictionary<string, double?>(_dialIns, StringComparer.OrdinalIgnoreCase);
        }
    }

    public void SetLocked(bool locked)
    {
        lock (_sync) { _locked = locked; }
    }

    public void ClearAll()
    {
        lock (_sync)
        {
            _pins.Clear();
            _dialIns.Clear();
            _locked = false;
        }
    }
}
