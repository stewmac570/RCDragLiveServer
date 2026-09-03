using Microsoft.AspNetCore.Mvc;
using RCDragLiveServer.Controllers;
using RCDragLiveServer.Models;
using RCDragLiveServer.Services;

namespace RCDragLiveServer.Tests.Controllers;

public sealed class DriverDialInControllerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Post_InvalidDialIn_ReturnsBadRequestWithoutRateLimitOrStoreWrite(double? dialIn)
    {
        var dialInStore = new RecordingDialInStore();
        var rateLimiter = new RecordingRateLimiter();
        var controller = new DriverDialInController(dialInStore, rateLimiter, new PassThroughStateStore());

        var result = controller.Post(new DriverDialInRequest
        {
            EventId = "evt1",
            DriverId = 12,
            DialIn = dialIn
        });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0, rateLimiter.CallCount);
        Assert.Equal(0, dialInStore.SubmitCount);
    }

    [Fact]
    public void Post_ValidDialIn_ResolvesEventKeyBeforeStoreWrite()
    {
        var dialInStore = new RecordingDialInStore();
        var controller = new DriverDialInController(
            dialInStore,
            new RecordingRateLimiter(),
            new FixedStateStore("resolved-event"));

        var result = controller.Post(new DriverDialInRequest
        {
            EventId = "session-guid",
            DriverId = 12,
            DialIn = 3.25,
            Pin = "1234"
        });

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("resolved-event", dialInStore.LastEventId);
        Assert.Equal(12, dialInStore.LastDriverId);
        Assert.Equal(3.25, dialInStore.LastDialIn);
    }

    [Theory]
    [InlineData("abcd")]
    [InlineData("123")]
    [InlineData("12345")]
    [InlineData("")]
    [InlineData(null)]
    public void Post_InvalidPinFormat_ReturnsBadRequestWithoutRateLimitOrStoreWrite(string? pin)
    {
        var dialInStore = new RecordingDialInStore();
        var rateLimiter = new RecordingRateLimiter();
        var controller = new DriverDialInController(dialInStore, rateLimiter, new PassThroughStateStore());

        var result = controller.Post(new DriverDialInRequest
        {
            EventId = "evt1",
            DriverId = 12,
            DialIn = 3.25,
            Pin = pin
        });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0, rateLimiter.CallCount);
        Assert.Equal(0, dialInStore.SubmitCount);
    }

    [Fact]
    public void Post_LockedEvent_StillSavesAndReportsPending()
    {
        var dialInStore = new RecordingDialInStore(lockedEventId: "resolved-event");
        var rateLimiter = new RecordingRateLimiter();
        var controller = new DriverDialInController(
            dialInStore,
            rateLimiter,
            new FixedStateStore("resolved-event"));

        var result = controller.Post(new DriverDialInRequest
        {
            EventId = "session-guid",
            DriverId = 12,
            DialIn = 3.25,
            Pin = "1234"
        });

        // A generated round must not block the driver: the time is stored either
        // way, and the response flags that it lands in the next race.
        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, rateLimiter.CallCount);
        Assert.Equal(1, dialInStore.SubmitCount);
    }

    [Fact]
    public void Post_DriverNotInEvent_ReturnsBadRequestWithoutRateLimitOrStoreWrite()
    {
        var dialInStore = new RecordingDialInStore();
        var rateLimiter = new RecordingRateLimiter();
        var controller = new DriverDialInController(
            dialInStore,
            rateLimiter,
            new FixedStateStore("resolved-event", driverExists: false));

        var result = controller.Post(new DriverDialInRequest
        {
            EventId = "session-guid",
            DriverId = 99,
            DialIn = 3.25,
            Pin = "1234"
        });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0, rateLimiter.CallCount);
        Assert.Equal(0, dialInStore.SubmitCount);
    }

    private sealed class RecordingDialInStore : IDialInStore
    {
        private readonly string? _lockedEventId;

        public RecordingDialInStore(string? lockedEventId = null) => _lockedEventId = lockedEventId;

        public int SubmitCount { get; private set; }
        public string? LastEventId { get; private set; }
        public int LastDriverId { get; private set; }
        public double? LastDialIn { get; private set; }

        public (bool success, string? error) VerifyPin(string eventId, int driverId, string? pin) => (true, null);

        public (bool success, string? error) SubmitUpdate(string eventId, int driverId, double? dialIn, string? pin)
        {
            SubmitCount++;
            LastEventId = eventId;
            LastDriverId = driverId;
            LastDialIn = dialIn;
            return (true, null);
        }

        public Dictionary<int, double?> GetAll(string eventId) => new();
        public void SetLocked(string eventId, bool locked) { }
        public bool IsLocked(string eventId) => string.Equals(eventId, _lockedEventId, StringComparison.OrdinalIgnoreCase);
        public void ClearAll(string eventId) { }
    }

    private sealed class RecordingRateLimiter : IDialInRateLimiter
    {
        public int CallCount { get; private set; }

        public bool TryAcquire(string eventId, int driverId)
        {
            CallCount++;
            return true;
        }
    }

    private class PassThroughStateStore : ILiveRaceStateStore
    {
        public LiveRaceState GetLatest() => new();
        public Dictionary<string, LiveRaceState> GetAll() => new();
        public void Upsert(LiveRaceState state) { }
        public void ClearEvent(string eventId, string? eventName) { }
        public IReadOnlyList<EventSummary> GetActiveEvents() => [];
        public Dictionary<string, LiveRaceState>? GetEvent(string eventId) => null;
        public virtual string ResolveEventKey(string eventId) => eventId;
        public virtual bool EventHasDriver(string eventId, int driverId) => true;
    }

    private sealed class FixedStateStore(string eventKey, bool driverExists = true) : PassThroughStateStore
    {
        public override string ResolveEventKey(string eventId) => eventKey;
        public override bool EventHasDriver(string eventId, int driverId) => driverExists;
    }
}
