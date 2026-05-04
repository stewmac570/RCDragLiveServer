namespace RCDragLiveServer.Models;

public class DriverDialInRequest
{
    public string DriverName { get; set; } = string.Empty;
    public double? DialIn { get; set; }
    public string? Pin { get; set; }
}
