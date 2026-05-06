namespace RCDragLiveServer.Services;

public interface IDialInStore
{
    (bool success, string? error) SubmitUpdate(string eventId, string driverName, double? dialIn, string? pin);
    Dictionary<string, double?> GetAll(string eventId);
    void SetLocked(string eventId, bool locked);
    bool IsLocked(string eventId);
    void ClearAll(string eventId);
}
