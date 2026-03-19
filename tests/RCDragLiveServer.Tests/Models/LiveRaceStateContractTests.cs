using RCDragLiveServer.Models;

namespace RCDragLiveServer.Tests.Models;

public class LiveRaceStateContractTests
{
    [Fact]
    public void LiveRaceState_Exposes_Required_Public_Properties()
    {
        var propertyNames = typeof(LiveRaceState)
            .GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("EventName", propertyNames);
        Assert.Contains("EventDate", propertyNames);
        Assert.Contains("CurrentRound", propertyNames);
        Assert.Contains("NextUp", propertyNames);
        Assert.Contains("Matches", propertyNames);
    }

    [Fact]
    public void Matches_Defaults_To_Empty_Collection()
    {
        var state = new LiveRaceState();

        Assert.NotNull(state.Matches);
        Assert.Equal(0, state.Matches.Count);
    }

    [Fact]
    public void LiveMatch_Exposes_Required_Public_Properties()
    {
        var propertyNames = typeof(LiveMatch)
            .GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("Driver1", propertyNames);
        Assert.Contains("Driver2", propertyNames);
    }
}
