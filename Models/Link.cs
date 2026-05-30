namespace KiirlinkServer.Models;

public class Link
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int? CategoryId { get; set; }
    public string OriginalUrl { get; set; } = string.Empty;
    public string ShortUrl { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool IsDeleted { get; set; } = false;

    public User User { get; set; } = null!;
    public Category? Category { get; set; }
    public ICollection<Favourite> Favourites { get; set; } = new List<Favourite>();
    public ICollection<LinkClick> LinkClicks { get; set; } = new List<LinkClick>();
    public ICollection<ActivityLog> ActivityLogs { get; set; } = new List<ActivityLog>();
}