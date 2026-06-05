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

    [Fact]
    public void Home_EventLinks_UrlEncodeEventIds()
    {
        var store = new StubStateStore(new[]
        {
            new EventSummary("Bob's Race & Test", "Bob's Race & Test", "2026-05-07", ClassCount: 1, LastUpdated: DateTime.UtcNow)
        });
        var controller = BuildController(store);

        var result = (ContentResult)controller.Home();

        Assert.Contains("href=\"/event/Bob%27s%20Race%20%26%20Test\"", result.Content!);
    }

    [Fact]
    public void GetEventPage_DialInForm_ShowsExistingDialInAndMobileInputHints()
    {
        var store = new StubStateStore(
            Array.Empty<EventSummary>(),
            new Dictionary<string, LiveRaceState>
            {
                ["2.5"] = new()
                {
                    EventId = "session-guid",
                    EventName = "Test Event",
                    EventDate = "2026-05-07",
                    ClassType = "2.5",
                    RaceType = "Dial-In",
                    CurrentRound = "R1",
                    Matches =
                    [
                        new LiveMatch
                        {
                            RoundLabel = "R1",
                            LeftDriverId = 12,
                            LeftDriver = "Stewart",
                            RightDriverId = 13,
                            RightDriver = "Alex",
                            LeftDriverDialIn = 3.25
                        }
                    ]
                }
            });
        var controller = BuildController(store);

        var result = (ContentResult)controller.GetEventPage("Test Event");

        Assert.Contains("<form class=\"dialin-form\" id=\"dialin-form\">", result.Content!);
        Assert.Contains("Existing dial-ins load automatically.", result.Content!);
        Assert.Contains("<select id=\"dialin-name\" required>", result.Content!);
        Assert.Contains("<option value=\"12\" data-name=\"Stewart\" data-dialin=\"3.250\">Stewart (3.250s)</option>", result.Content!);
        Assert.Contains("data-driver-id=\"12\"", result.Content!);
        Assert.Contains("inputmode=\"decimal\"", result.Content!);
        Assert.Contains("id=\"dialin-value\" step=\"0.001\" min=\"0.001\" inputmode=\"decimal\" autocomplete=\"off\" required", result.Content!);
        Assert.Contains("inputmode=\"numeric\"", result.Content!);
        Assert.Contains("pattern=\"[0-9]{4}\"", result.Content!);
        Assert.Contains("id=\"dialin-status\" role=\"status\" aria-live=\"polite\"", result.Content!);
        Assert.Contains("return r.text().then", result.Content!);
        Assert.Contains("Too many updates", result.Content!);
        Assert.Contains("Driver is no longer active in this event.", result.Content!);
        Assert.Contains("invalid_dialin", result.Content!);
        Assert.Contains("var DIALIN_KEY = 'rcDialInForm:' + PAGE_EVENT_ID;", result.Content!);
        Assert.Contains("updateVisibleDialIn(driverId, saved);", result.Content!);
        Assert.Contains("document.querySelectorAll('[data-driver-id=\"' + driverId + '\"]')", result.Content!);
        Assert.DoesNotContain("pin: pinEl", result.Content!);
        Assert.DoesNotContain("s.pin", result.Content!);
    }

    [Fact]
    public void GetEventPage_PageEventId_IsJavaScriptEncoded()
    {
        var eventKey = "Bob's \"Race\"";
        var store = new StubStateStore(
            Array.Empty<EventSummary>(),
            new Dictionary<string, LiveRaceState>
            {
                ["2.5"] = new()
                {
                    EventId = "session-guid",
                    EventName = eventKey,
                    EventDate = "2026-05-07",
                    ClassType = "2.5",
                    RaceType = "Dial-In",
                    CurrentRound = "R1",
                    Matches =
                    [
                        new LiveMatch
                        {
                            RoundLabel = "R1",
                            LeftDriverId = 12,
                            LeftDriver = "Stewart",
                            RightDriverId = 13,
                            RightDriver = "Alex"
                        }
                    ]
                }
            },
            resolvedEventKey: eventKey);
        var controller = BuildController(store);

        var result = (ContentResult)controller.GetEventPage("session-guid");

        Assert.Contains("var PAGE_EVENT_ID = ", result.Content!);
        Assert.Contains("\\u0027", result.Content!);
        Assert.Contains("\\u0022Race\\u0022", result.Content!);
        Assert.DoesNotContain("var PAGE_EVENT_ID = '", result.Content!);
    }

    [Fact]
    public void GetEventPage_DialInLock_UsesResolvedEventKey()
    {
        var store = new StubStateStore(
            Array.Empty<EventSummary>(),
            new Dictionary<string, LiveRaceState>
            {
                ["2.5"] = new()
                {
                    EventId = "session-guid",
                    EventName = "Test Event",
                    EventDate = "2026-05-07",
                    ClassType = "2.5",
                    RaceType = "Dial-In",
                    CurrentRound = "R1",
                    Matches =
                    [
                        new LiveMatch
                        {
                            RoundLabel = "R1",
                            LeftDriverId = 12,
                            LeftDriver = "Stewart",
                            RightDriverId = 13,
                            RightDriver = "Alex"
                        }
                    ]
                }
            },
            resolvedEventKey: "resolved-event");
        var controller = BuildController(store, new StubDialInStore(lockedEventId: "resolved-event"));

        var result = (ContentResult)controller.GetEventPage("session-guid");

        Assert.Contains("dialin-locked-notice", result.Content!);
        Assert.DoesNotContain("id=\"dialin-form\"", result.Content!);
    }

    [Fact]
    public void GetEventPage_UsesSubmittedDialInBeforeDesktopPushRefreshesLiveState()
    {
        var store = new StubStateStore(
            Array.Empty<EventSummary>(),
            new Dictionary<string, LiveRaceState>
            {
                ["2.5"] = new()
                {
                    EventId = "session-guid",
                    EventName = "Test Event",
                    EventDate = "2026-05-07",
                    ClassType = "2.5",
                    RaceType = "Dial-In",
                    CurrentRound = "R1",
                    Matches =
                    [
                        new LiveMatch
                        {
                            RoundLabel = "R1",
                            LeftDriverId = 12,
                            LeftDriver = "Stewart",
                            RightDriverId = 13,
                            RightDriver = "Alex",
                            LeftDriverDialIn = 3.1
                        }
                    ]
                }
            },
            resolvedEventKey: "resolved-event");
        var dialIns = new StubDialInStore(new Dictionary<int, double?> { [12] = 3.275 });
        var controller = BuildController(store, dialIns);

        var result = (ContentResult)controller.GetEventPage("session-guid");

        Assert.Contains("<span class=\"dial-in-badge\">3.275s</span>", result.Content!);
        Assert.Contains("<option value=\"12\" data-name=\"Stewart\" data-dialin=\"3.275\">Stewart (3.275s)</option>", result.Content!);
        Assert.DoesNotContain("Stewart (3.100s)", result.Content!);
    }

    private static PublicLiveController BuildController(ILiveRaceStateStore store, IDialInStore? dialInStore = null)
    {
        var controller = new PublicLiveController(store, dialInStore ?? new StubDialInStore());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return controller;
    }

    private sealed class StubStateStore : ILiveRaceStateStore
    {
        private readonly IReadOnlyList<EventSummary> _events;
        private readonly Dictionary<string, LiveRaceState>? _classes;
        private readonly string? _resolvedEventKey;

        public StubStateStore(
            IReadOnlyList<EventSummary> events,
            Dictionary<string, LiveRaceState>? classes = null,
            string? resolvedEventKey = null)
        {
            _events = events;
            _classes = classes;
            _resolvedEventKey = resolvedEventKey;
        }

        public IReadOnlyList<EventSummary> GetActiveEvents() => _events;
        public LiveRaceState GetLatest() => new();
        public Dictionary<string, LiveRaceState> GetAll() => new();
        public void Upsert(LiveRaceState state) { }
        public void ClearEvent(string eventId, string? eventName) { }
        public Dictionary<string, LiveRaceState>? GetEvent(string eventId) => _classes;
        public string ResolveEventKey(string eventId) => _resolvedEventKey ?? eventId;
        public bool EventHasDriver(string eventId, int driverId) => true;
    }

    private sealed class StubDialInStore : IDialInStore
    {
        private readonly string? _lockedEventId;
        private readonly Dictionary<int, double?> _dialIns;

        public StubDialInStore(Dictionary<int, double?>? dialIns = null, string? lockedEventId = null)
        {
            _dialIns = dialIns ?? new Dictionary<int, double?>();
            _lockedEventId = lockedEventId;
        }

        public (bool success, string? error) SubmitUpdate(string eventId, int driverId, double? dialIn, string? pin) => (true, null);
        public Dictionary<int, double?> GetAll(string eventId) => new(_dialIns);
        public void SetLocked(string eventId, bool locked) { }
        public bool IsLocked(string eventId) => string.Equals(eventId, _lockedEventId, StringComparison.OrdinalIgnoreCase);
        public void ClearAll(string eventId) { }
    }
}
