namespace RCDragLiveServer.Services;

public interface IDialInStore
{
    (bool success, string? error) SubmitUpdate(string driverName, double? dialIn, string? pin);
    Dictionary<string, double?> GetAll();
    void SetLocked(bool locked);
    bool IsLocked { get; }
    void ClearAll();
}
