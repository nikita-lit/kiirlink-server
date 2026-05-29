namespace KiirlinkServer.Models;

public class Favourite
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int LinkId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public Link Link { get; set; } = null!;
}