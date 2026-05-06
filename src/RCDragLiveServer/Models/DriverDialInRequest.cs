namespace RCDragLiveServer.Models;

public class DriverDialInRequest
{
    public string EventId { get; set; } = string.Empty;
    public int DriverId { get; set; }
    public double? DialIn { get; set; }
    public string? Pin { get; set; }
}
