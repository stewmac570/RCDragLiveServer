using RCDragLiveServer.Services;

namespace RCDragLiveServer.Tests.Services;

public sealed class InMemoryDialInRateLimiterTests
{
    [Fact]
    public void TryAcquire_SameEventAndDriver_RateLimitedWithinCooldown()
    {
        var limiter = new InMemoryDialInRateLimiter();

        Assert.True(limiter.TryAcquire("event-one", 12));
        Assert.False(limiter.TryAcquire("event-one", 12));
    }

    [Fact]
    public void TryAcquire_SameDriverDifferentEvents_Allowed()
    {
        var limiter = new InMemoryDialInRateLimiter();

        Assert.True(limiter.TryAcquire("event-one", 12));
        Assert.True(limiter.TryAcquire("event-two", 12));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void TryAcquire_InvalidEvent_Rejected(string eventId)
    {
        var limiter = new InMemoryDialInRateLimiter();

        Assert.False(limiter.TryAcquire(eventId, 12));
    }

    [Fact]
    public void TryAcquire_InvalidDriver_Rejected()
    {
        var limiter = new InMemoryDialInRateLimiter();

        Assert.False(limiter.TryAcquire("event-one", 0));
    }
}
