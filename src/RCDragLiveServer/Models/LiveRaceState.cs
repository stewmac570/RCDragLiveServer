namespace RCDragLiveServer.Models;

public class LiveRaceState
{
    public string EventName { get; set; } = string.Empty;
    public string EventDate { get; set; } = string.Empty;
    public string CurrentRound { get; set; } = string.Empty;
    public string NextUp { get; set; } = string.Empty;
    public List<LiveMatch> Matches { get; set; } = new();
}
