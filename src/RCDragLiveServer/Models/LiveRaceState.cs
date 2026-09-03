namespace RCDragLiveServer.Models;

public class LiveRaceState
{
    public string EventId { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public string EventDate { get; set; } = string.Empty;
    public string ClassType { get; set; } = string.Empty;
    public string RaceType { get; set; } = string.Empty;
    public string CurrentRound { get; set; } = string.Empty;
    public string NextUp { get; set; } = string.Empty;
    public string? RRStandings { get; set; }
    // Entry list, sent before any bracket exists. Without it the site has no
    // driver names to offer until the first round is generated.
    public List<LiveDriverEntry> Drivers { get; set; } = new();
    public List<LiveMatch> Matches { get; set; } = new();
    public List<LiveWinner> Winners { get; set; } = new();
    public bool DialInLocked { get; set; }
}
