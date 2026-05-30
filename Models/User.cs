using Microsoft.AspNetCore.Identity;

namespace KiirlinkServer.Models;

public class User : IdentityUser
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Link> Links { get; set; } = [];
    public List<Favourite> Favourites { get; set; } = [];
}