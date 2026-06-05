using RCDragLiveServer.Models;
using RCDragLiveServer.Services;

namespace RCDragLiveServer.Tests.Services;

public sealed class InMemoryLiveRaceStateStoreTests
{
    [Fact]
    public void ResolveEventKey_SessionGuidAliasesToEventNameBucket()
    {
        var dialIns = new InMemoryDialInStore();
        var store = new InMemoryLiveRaceStateStore(dialIns);
        var sessionId = Guid.NewGuid();

        store.Upsert(new LiveRaceState
        {
            EventId = sessionId.ToString("N"),
            EventName = "Saturday Shootout",
            ClassType = "2.5",
            Matches = [new LiveMatch()]
        });

        var eventKey = store.ResolveEventKey(sessionId.ToString("D"));

        Assert.Equal("Saturday Shootout", eventKey);
        Assert.NotNull(store.GetEvent(sessionId.ToString("D")));
    }

    [Fact]
    public void Upsert_NewSessionForSameEventName_ClearsOldDialIns()
    {
        var dialIns = new InMemoryDialInStore();
        var store = new InMemoryLiveRaceStateStore(dialIns);

        store.Upsert(new LiveRaceState
        {
            EventId = Guid.NewGuid().ToString("N"),
            EventName = "Saturday Shootout",
            ClassType = "2.5",
            Matches = [new LiveMatch()]
        });
        Assert.True(dialIns.SubmitUpdate("Saturday Shootout", 7, 3.250, null).success);

        store.Upsert(new LiveRaceState
        {
            EventId = Guid.NewGuid().ToString("N"),
            EventName = "Saturday Shootout",
            ClassType = "2.5",
            Matches = [new LiveMatch()]
        });

        Assert.Empty(dialIns.GetAll("Saturday Shootout"));
    }

    [Fact]
    public void ClearEvent_ClearsDialInsForResolvedSessionGuid()
    {
        var dialIns = new InMemoryDialInStore();
        var store = new InMemoryLiveRaceStateStore(dialIns);
        var sessionId = Guid.NewGuid();

        store.Upsert(new LiveRaceState
        {
            EventId = sessionId.ToString("N"),
            EventName = "Saturday Shootout",
            ClassType = "2.5",
            Matches = [new LiveMatch()]
        });
        Assert.True(dialIns.SubmitUpdate("Saturday Shootout", 7, 3.250, null).success);

        store.ClearEvent(sessionId.ToString("D"), eventName: null);

        Assert.Empty(dialIns.GetAll("Saturday Shootout"));
        Assert.Null(store.GetEvent("Saturday Shootout"));
    }

    [Fact]
    public void EventHasDriver_UsesResolvedEventKeyAndCurrentMatches()
    {
        var dialIns = new InMemoryDialInStore();
        var store = new InMemoryLiveRaceStateStore(dialIns);
        var sessionId = Guid.NewGuid();

        store.Upsert(new LiveRaceState
        {
            EventId = sessionId.ToString("N"),
            EventName = "Saturday Shootout",
            ClassType = "2.5",
            Matches =
            [
                new LiveMatch
                {
                    LeftDriverId = 7,
                    RightDriverId = 8
                }
            ]
        });

        Assert.True(store.EventHasDriver(sessionId.ToString("D"), 7));
        Assert.True(store.EventHasDriver("Saturday Shootout", 8));
        Assert.False(store.EventHasDriver("Saturday Shootout", 99));
        Assert.False(store.EventHasDriver("missing-event", 7));
    }
}
