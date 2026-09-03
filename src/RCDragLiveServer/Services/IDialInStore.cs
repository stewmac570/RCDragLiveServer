namespace RCDragLiveServer.Services;

public interface IDialInStore
{
    (bool success, string? error) SubmitUpdate(string eventId, int driverId, double? dialIn, string? pin);
    (bool success, string? error) VerifyPin(string eventId, int driverId, string? pin);
    Dictionary<int, double?> GetAll(string eventId);
    void SetLocked(string eventId, bool locked);
    bool IsLocked(string eventId);
    void ClearAll(string eventId);
}
