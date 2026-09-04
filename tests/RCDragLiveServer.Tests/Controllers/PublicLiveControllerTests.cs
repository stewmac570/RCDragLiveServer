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

        Assert.Contains("<form id=\"dialin-login\">", result.Content!);
        Assert.Contains("<select id=\"dialin-name\" required></select>", result.Content!);
        Assert.Contains("id=\"dialin-panel\"", result.Content!);
        Assert.Contains("id=\"dialin-logout\"", result.Content!);
        Assert.Contains("\"name\":\"Stewart\",\"dialIn\":\"3.250\"", result.Content!);
        Assert.Contains("data-driver-id=\"12\"", result.Content!);
        Assert.Contains("inputmode=\"decimal\"", result.Content!);
        Assert.Contains("inputmode=\"numeric\"", result.Content!);
        Assert.Contains("pattern=\"[0-9]{4}\"", result.Content!);
        Assert.Contains("id=\"dialin-status\" role=\"status\" aria-live=\"polite\"", result.Content!);
        Assert.Contains("/api/dialin/login", result.Content!);
        Assert.Contains("Incorrect PIN.", result.Content!);
        // The PIN lives in a closure variable only; it is never persisted.
        Assert.DoesNotContain("sessionStorage.setItem(STATE_KEY", result.Content!);
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

        // The driver can always save; only the notice changes.
        Assert.Contains("id=\"dialin-notice\"", result.Content!);
        Assert.Contains("id=\"dialin-login\"", result.Content!);
        Assert.Contains("id=\"dialin-form\"", result.Content!);
        Assert.Contains("\"locked\":true", result.Content!);
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
        Assert.Contains("\"name\":\"Stewart\",\"dialIn\":\"3.275\"", result.Content!);
        Assert.DoesNotContain("\"dialIn\":\"3.100\"", result.Content!);
    }

    // The landing page is where drivers actually arrive, so the dial-in form has to
    // live there too -- not only on /event/{id}.
    [Fact]
    public void Home_ActiveEventWithDrivers_RendersDialInForm()
    {
        var store = new StubStateStore(
            new[] { new EventSummary("Test Event", "Test Event", "2026-05-07", ClassCount: 1, LastUpdated: DateTime.UtcNow) },
            classesByEvent: new Dictionary<string, Dictionary<string, LiveRaceState>>
            {
                ["Test Event"] = ClassesWith("Test Event", (12, "Stewart"), (13, "Alex"))
            });
        var controller = BuildController(store);

        var result = (ContentResult)controller.Home();

        Assert.Contains("<form id=\"dialin-form\">", result.Content!);
        Assert.Contains("<select id=\"dialin-name\" required></select>", result.Content!);
        Assert.Contains("id=\"dialin-pin\"", result.Content!);
        Assert.Contains("\"name\":\"Stewart\"", result.Content!);
        Assert.Contains("\"name\":\"Alex\"", result.Content!);
        Assert.Contains("\"eventKey\":\"Test Event\"", result.Content!);
        Assert.DoesNotContain("tab-bar", result.Content!);
    }

    [Fact]
    public void Home_SubmittedDialIn_PrefillsRosterEntry()
    {
        var store = new StubStateStore(
            new[] { new EventSummary("Test Event", "Test Event", "2026-05-07", ClassCount: 1, LastUpdated: DateTime.UtcNow) },
            classesByEvent: new Dictionary<string, Dictionary<string, LiveRaceState>>
            {
                ["Test Event"] = ClassesWith("Test Event", (12, "Stewart"), (13, "Alex"))
            });
        var dialIns = new StubDialInStore(new Dictionary<int, double?> { [12] = 3.275 });
        var controller = BuildController(store, dialIns);

        var result = (ContentResult)controller.Home();

        Assert.Contains("\"dialIn\":\"3.275\"", result.Content!);
    }

    [Fact]
    public void Home_LockedEvent_MarksEventLockedInPayload()
    {
        var store = new StubStateStore(
            new[] { new EventSummary("Test Event", "Test Event", "2026-05-07", ClassCount: 1, LastUpdated: DateTime.UtcNow) },
            classesByEvent: new Dictionary<string, Dictionary<string, LiveRaceState>>
            {
                ["Test Event"] = ClassesWith("Test Event", (12, "Stewart"), (13, "Alex"))
            });
        var controller = BuildController(store, new StubDialInStore(lockedEventId: "Test Event"));

        var result = (ContentResult)controller.Home();

        Assert.Contains("\"locked\":true", result.Content!);
        Assert.Contains("id=\"dialin-notice\"", result.Content!);
        // The picker must stay outside the form so a lock on one event cannot strand
        // a driver whose own event is still open.
        assertPickerOutsideForm(result.Content!);
    }

    // With more than one event running, the driver has to say which one they are in.
    [Fact]
    public void Home_MultipleEvents_ShowsEventPickerWithBothRosters()
    {
        var store = new StubStateStore(
            new[]
            {
                new EventSummary("Event One", "Event One", "2026-05-07", ClassCount: 1, LastUpdated: DateTime.UtcNow),
                new EventSummary("Event Two", "Event Two", "2026-05-07", ClassCount: 1, LastUpdated: DateTime.UtcNow)
            },
            classesByEvent: new Dictionary<string, Dictionary<string, LiveRaceState>>
            {
                ["Event One"] = ClassesWith("Event One", (12, "Stewart"), (13, "Alex")),
                ["Event Two"] = ClassesWith("Event Two", (21, "Jordan"), (22, "Sam"))
            });
        var controller = BuildController(store);

        var result = (ContentResult)controller.Home();

        Assert.DoesNotContain("<div hidden>", result.Content!);
        Assert.Contains("<label for=\"dialin-event\">Event</label>", result.Content!);
        Assert.Contains("<option value=\"0\">Event One</option>", result.Content!);
        Assert.Contains("<option value=\"1\">Event Two</option>", result.Content!);
        Assert.Contains("\"name\":\"Jordan\"", result.Content!);
    }

    [Fact]
    public void Home_SingleEvent_HidesEventPicker()
    {
        var store = new StubStateStore(
            new[] { new EventSummary("Test Event", "Test Event", "2026-05-07", ClassCount: 1, LastUpdated: DateTime.UtcNow) },
            classesByEvent: new Dictionary<string, Dictionary<string, LiveRaceState>>
            {
                ["Test Event"] = ClassesWith("Test Event", (12, "Stewart"), (13, "Alex"))
            });
        var controller = BuildController(store);

        var result = (ContentResult)controller.Home();

        Assert.Contains("<div hidden>", result.Content!);
        Assert.Contains("<option value=\"0\">Test Event</option>", result.Content!);
    }

    [Fact]
    public void Home_NoActiveEvents_OmitsDialInForm()
    {
        var store = new StubStateStore(Array.Empty<EventSummary>());
        var controller = BuildController(store);

        var result = (ContentResult)controller.Home();

        Assert.DoesNotContain("id=\"dialin-form\"", result.Content!);
        Assert.Contains("var DIALIN_EVENTS = [];", result.Content!);
    }

    // BYE placeholders and id-less rows are bracket filler, not people who can dial in.
    [Fact]
    public void Home_ByeAndUnknownDrivers_ExcludedFromRoster()
    {
        var store = new StubStateStore(
            new[] { new EventSummary("Test Event", "Test Event", "2026-05-07", ClassCount: 1, LastUpdated: DateTime.UtcNow) },
            classesByEvent: new Dictionary<string, Dictionary<string, LiveRaceState>>
            {
                ["Test Event"] = ClassesWith("Test Event", (12, "Stewart"), (13, "BYE"))
            });
        var controller = BuildController(store);

        var result = (ContentResult)controller.Home();

        Assert.Contains("\"name\":\"Stewart\"", result.Content!);
        Assert.DoesNotContain("\"name\":\"BYE\"", result.Content!);
    }

    // Event names are user-supplied and land inside a <script> block.
    [Fact]
    public void Home_DialInPayload_EscapesScriptSensitiveCharacters()
    {
        var store = new StubStateStore(
            new[] { new EventSummary("Bob's <Race>", "Bob's <Race>", "2026-05-07", ClassCount: 1, LastUpdated: DateTime.UtcNow) },
            classesByEvent: new Dictionary<string, Dictionary<string, LiveRaceState>>
            {
                ["Bob's <Race>"] = ClassesWith("Bob's <Race>", (12, "</script>"), (13, "Alex"))
            });
        var controller = BuildController(store);

        var result = (ContentResult)controller.Home();

        Assert.DoesNotContain("</script>\"", result.Content!);
        Assert.Contains("\\u003C", result.Content!);
    }

    private static void assertPickerOutsideForm(string html)
    {
        var pickerIndex = html.IndexOf("id=\"dialin-event\"", StringComparison.Ordinal);
        var formIndex = html.IndexOf("<form id=\"dialin-login\">", StringComparison.Ordinal);
        Assert.True(pickerIndex >= 0 && formIndex >= 0);
        Assert.True(pickerIndex < formIndex, "Event picker must be rendered before (outside) the dial-in form.");
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

    private static Dictionary<string, LiveRaceState> ClassesWith(string eventName, params (int Id, string Name)[] drivers)
    {
        var matches = new List<LiveMatch>();
        for (int i = 0; i < drivers.Length; i += 2)
        {
            matches.Add(new LiveMatch
            {
                RoundLabel = "R1",
                LeftDriverId = drivers[i].Id,
                LeftDriver = drivers[i].Name,
                RightDriverId = i + 1 < drivers.Length ? drivers[i + 1].Id : 0,
                RightDriver = i + 1 < drivers.Length ? drivers[i + 1].Name : string.Empty
            });
        }

        return new Dictionary<string, LiveRaceState>
        {
            ["2.5"] = new()
            {
                EventId = "session-guid",
                EventName = eventName,
                EventDate = "2026-05-07",
                ClassType = "2.5",
                RaceType = "Dial-In",
                CurrentRound = "R1",
                Matches = matches
            }
        };
    }

    private sealed class StubStateStore : ILiveRaceStateStore
    {
        private readonly IReadOnlyList<EventSummary> _events;
        private readonly Dictionary<string, LiveRaceState>? _classes;
        private readonly string? _resolvedEventKey;
        private readonly Dictionary<string, Dictionary<string, LiveRaceState>>? _classesByEvent;

        public StubStateStore(
            IReadOnlyList<EventSummary> events,
            Dictionary<string, LiveRaceState>? classes = null,
            string? resolvedEventKey = null,
            Dictionary<string, Dictionary<string, LiveRaceState>>? classesByEvent = null)
        {
            _events = events;
            _classes = classes;
            _resolvedEventKey = resolvedEventKey;
            _classesByEvent = classesByEvent;
        }

        public IReadOnlyList<EventSummary> GetActiveEvents() => _events;
        public LiveRaceState GetLatest() => new();
        public Dictionary<string, LiveRaceState> GetAll() => new();
        public void Upsert(LiveRaceState state) { }
        public void ClearEvent(string eventId, string? eventName) { }

        public Dictionary<string, LiveRaceState>? GetEvent(string eventId)
        {
            if (_classesByEvent is not null)
                return _classesByEvent.TryGetValue(eventId, out var byEvent) ? byEvent : null;

            return _classes;
        }

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
        public (bool success, string? error) VerifyPin(string eventId, int driverId, string? pin) => (true, null);
        public Dictionary<int, double?> GetAll(string eventId) => new(_dialIns);
        public void SetLocked(string eventId, bool locked) { }
        public bool IsLocked(string eventId) => string.Equals(eventId, _lockedEventId, StringComparison.OrdinalIgnoreCase);
        public void ClearAll(string eventId) { }
    }
}
