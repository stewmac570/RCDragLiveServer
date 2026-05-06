using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RCDragLiveServer.Controllers;
using RCDragLiveServer.Models;
using RCDragLiveServer.Services;

namespace RCDragLiveServer.Tests.Controllers;

public sealed class PublicLiveControllerTests
{
    // Verifies that the landing page is always returned from Home() regardless of
    // how many classes/events are active. Regression guard for the single-class
    // bypass bug (issue #211) where events.Count == 1 skipped straight to the bracket.

    [Fact]
    public void Home_SingleClassEvent_ReturnsLandingPage()
    {
        var store = new StubStateStore(new[]
        {
            new EventSummary("evt1", "Test Event", "2026-05-07", ClassCount: 1, LastUpdated: DateTime.UtcNow)
        });
        var controller = BuildController(store);

        var result = (ContentResult)controller.Home();

        Assert.Contains("Stew Mac RC", result.Content!);
        Assert.Contains("/event/evt1", result.Content!);
        Assert.DoesNotContain("tab-bar", result.Content!);
    }

    [Fact]
    public void Home_NoActiveEvents_ReturnsLandingPage()
    {
        var store = new StubStateStore(Array.Empty<EventSummary>());
        var controller = BuildController(store);

        var result = (ContentResult)controller.Home();

        Assert.Contains("Stew Mac RC", result.Content!);
        Assert.Contains("No active events", result.Content!);
    }

    [Fact]
    public void Home_MultipleEvents_ReturnsLandingPageWithAllCards()
    {
        var store = new StubStateStore(new[]
        {
            new EventSummary("evt1", "Event One", "2026-05-07", ClassCount: 2, LastUpdated: DateTime.UtcNow),
            new EventSummary("evt2", "Event Two", "2026-05-07", ClassCount: 1, LastUpdated: DateTime.UtcNow)
        });
        var controller = BuildController(store);

        var result = (ContentResult)controller.Home();

        Assert.Contains("Stew Mac RC", result.Content!);
        Assert.Contains("/event/evt1", result.Content!);
        Assert.Contains("/event/evt2", result.Content!);
    }

    private static PublicLiveController BuildController(ILiveRaceStateStore store)
    {
        var controller = new PublicLiveController(store, new StubDialInStore());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return controller;
    }

    private sealed class StubStateStore : ILiveRaceStateStore
    {
        private readonly IReadOnlyList<EventSummary> _events;
        public StubStateStore(IReadOnlyList<EventSummary> events) => _events = events;
        public IReadOnlyList<EventSummary> GetActiveEvents() => _events;
        public LiveRaceState GetLatest() => new();
        public Dictionary<string, LiveRaceState> GetAll() => new();
        public void Upsert(LiveRaceState state) { }
        public void ClearEvent(string eventId, string? eventName) { }
        public Dictionary<string, LiveRaceState>? GetEvent(string eventId) => null;
    }

    private sealed class StubDialInStore : IDialInStore
    {
        public (bool success, string? error) SubmitUpdate(string eventId, int driverId, double? dialIn, string? pin) => (true, null);
        public Dictionary<int, double?> GetAll(string eventId) => new();
        public void SetLocked(string eventId, bool locked) { }
        public bool IsLocked(string eventId) => false;
        public void ClearAll(string eventId) { }
    }
}
