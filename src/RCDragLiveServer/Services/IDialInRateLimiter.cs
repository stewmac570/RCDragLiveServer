namespace RCDragLiveServer.Services;

public interface IDialInRateLimiter
{
    bool TryAcquire(string eventId, int driverId);
}
