namespace RCDragLiveServer.Services;

public interface IDialInRateLimiter
{
    bool TryAcquire(int driverId);
}
