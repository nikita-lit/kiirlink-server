namespace KiirlinkServer.Models;

public class ActivityLog
{
    public int Id { get; set; }
    public int LinkId { get; set; }
    public string Action { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public Link Link { get; set; } = null!;
}