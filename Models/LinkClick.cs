namespace KiirlinkServer.Models;

public class LinkClick
{
    public int Id { get; set; }
    public int LinkId { get; set; }
    public DateTime ClickedAt { get; set; } = DateTime.UtcNow;
    public string? DeviceType { get; set; }
    public string? Source { get; set; }
    public string? Country { get; set; }

    public Link Link { get; set; } = null!;
}