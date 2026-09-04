namespace RCDragLiveServer.Models;

public class LiveDriverEntry
{
    public int DriverId { get; set; }
    public string DriverName { get; set; } = string.Empty;
    public double? DialIn { get; set; }
}
