using RCDragLiveServer.Controllers;
using RCDragLiveServer.Models;
using RCDragLiveServer.Services;

namespace RCDragLiveServer.Tests.Services;

/// <summary>
/// The scoreboard stopped reloading itself on a timer, so a change that does not
/// reach subscribers is a board that never updates. These cover the wiring.
/// </summary>
public sealed class LiveUpdatePushTests
{
    [Fact]
    public async Task Publish_WakesASubscriberWaitingOnThatEvent()
    {
        var broadcaster = new InMemoryLiveUpdateBroadcaster();
        using var subscription = broadcaster.Subscribe("evt1");

        var waiting = subscription.WaitForChangeAsync(CancellationToken.None);
        broadcaster.Publish("evt1");

        Assert.True(await waiting);
    }

    [Fact]
    public async Task Publish_DoesNotWakeSubscribersOfOtherEvents()
    {
        var broadcaster = new InMemoryLiveUpdateBroadcaster();
        using var subscription = broadcaster.Subscribe("evt1");

        var waiting = subscription.WaitForChangeAsync(CancellationToken.None);
        broadcaster.Publish("a-different-event");

        var finished = await Task.WhenAny(waiting, Task.Delay(150));
        Assert.NotSame(waiting, finished);
    }

    // A burst from the desktop -- winner submitted, round advanced, bracket redrawn --
    // should cost the page one refresh, not three.
    [Fact]
    public async Task RepeatedPublishesCollapseIntoOneWakeUp()
    {
        var broadcaster = new InMemoryLiveUpdateBroadcaster();
        using var subscription = broadcaster.Subscribe("evt1");

        broadcaster.Publish("evt1");
        broadcaster.Publish("evt1");
        broadcaster.Publish("evt1");

        Assert.True(await subscription.WaitForChangeAsync(CancellationToken.None));

        var second = subscription.WaitForChangeAsync(CancellationToken.None);
        var finished = await Task.WhenAny(second, Task.Delay(150));
        Assert.NotSame(second, finished);
    }

    [Fact]
    public void Dispose_RemovesTheSubscriber()
    {
        var broadcaster = new InMemoryLiveUpdateBroadcaster();

        var subscription = broadcaster.Subscribe("evt1");
        Assert.Equal(1, broadcaster.SubscriberCount);

        subscription.Dispose();
        Assert.Equal(0, broadcaster.SubscriberCount);
    }

    [Fact]
    public void DesktopPush_PublishesTheEventAndTheLandingPage()
    {
        var broadcaster = new RecordingBroadcaster();
        var store = new InMemoryLiveRaceStateStore(new InMemoryDialInStore(), broadcaster);

        store.Upsert(new LiveRaceState
        {
            EventId = "session-guid",
            EventName = "Saturday Shootout",
            ClassType = "2.5",
            Matches = new List<LiveMatch>()
        });

        Assert.Contains("Saturday Shootout", broadcaster.Published);
        Assert.Contains(PublicLiveController.LandingStreamKey, broadcaster.Published);
    }

    [Fact]
    public void ClearingAFinishedEvent_AlsoWakesWatchers()
    {
        var broadcaster = new RecordingBroadcaster();
        var store = new InMemoryLiveRaceStateStore(new InMemoryDialInStore(), broadcaster);

        store.Upsert(new LiveRaceState
        {
            EventId = "session-guid",
            EventName = "Saturday Shootout",
            ClassType = "2.5",
            Matches = new List<LiveMatch>()
        });
        broadcaster.Published.Clear();

        store.ClearEvent("session-guid", "Saturday Shootout");

        Assert.Contains("Saturday Shootout", broadcaster.Published);
        Assert.Contains(PublicLiveController.LandingStreamKey, broadcaster.Published);
    }
}
