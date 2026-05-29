using System.Security.Claims;
using System.Text.Json;
using KiirlinkServer.Models;
using Microsoft.EntityFrameworkCore;

namespace KiirlinkServer.Endpoints;

public static class LinkEndpoints
{
    public static void MapLinkEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/links");

        group.MapPost("/shorten", ShortenLongUrl)
            .RequireAuthorization()
            .WithName("CreateShortUrl");
        
        group.MapPost("/get", GetUrls)
            .RequireAuthorization()
            .WithName("GetUrls");
        
        group.MapPost("/remove", RemoveShortUrl)
            .RequireAuthorization()
            .WithName("RemoveShortUrl");
        
        app.MapGet("/{shortUrl}", RedirectToOriginalUrl)
            .WithName("RedirectToOriginalUrl");
    }

    private static async Task<IResult> ShortenLongUrl(
        string longUrl, 
        DbContext db, 
        ClaimsPrincipal user)
    {
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) 
            return Results.Unauthorized();

        var shortUrl = Guid.NewGuid().ToString()[..6];
        var newLink = new Link
        {
            OriginalUrl = longUrl,
            ShortUrl = shortUrl,
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
        };

        db.Links.Add(newLink);
        await db.SaveChangesAsync();
        
        var log = new ActivityLog
        {
            LinkId = newLink.Id,
            Action = $"The user {user.Identity?.Name} created the link {newLink.ShortUrl}, which leads to {newLink.OriginalUrl}",
            CreatedAt = DateTime.UtcNow
        };
        db.ActivityLogs.Add(log);
        await db.SaveChangesAsync();

        return Results.Created($"/api/links/{shortUrl}", new { newLink.ShortUrl, newLink.OriginalUrl });
    }

    private static async Task<IResult> GetUrls(
        DbContext db, 
        ClaimsPrincipal user)
    {
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) 
            return Results.Unauthorized();
        
        var links = await db.Links
            .Where(l => !l.IsDeleted)
            .Where(l => l.UserId == userId)
            .Select(l => new { l.Id, l.OriginalUrl, l.ShortUrl, l.CreatedAt })
            .ToListAsync();
        
        return Results.Ok(links);
    }

    private static async Task<IResult> RemoveShortUrl(
        int linkId, 
        DbContext db, 
        ClaimsPrincipal user)
    {
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) 
            return Results.Unauthorized();
        
        var link = await db.Links
            .FirstOrDefaultAsync(l => l.Id == linkId && l.UserId == userId);
        
        if (link == null)
            return Results.NotFound(new { Message = "The link was not found or is out of date." });
        
        link.IsDeleted = true;
        
        var log = new ActivityLog
        {
            LinkId = link.Id,
            Action = $"The user {user.Identity?.Name} deleted the link {link.ShortUrl}, which led to {link.OriginalUrl}",
            CreatedAt = DateTime.UtcNow
        };
        
        db.ActivityLogs.Add(log);
        
        await db.SaveChangesAsync();
        
        // "Deleted"
        return Results.Ok(new { Message = "Link successfully deleted." });
    }
    
    private static async Task<IResult> RedirectToOriginalUrl(
        string shortUrl, 
        DbContext db, 
        HttpContext httpContext,
        IHttpClientFactory clientFactory)
    { 
        var link = await db.Links
            .Where(l => !l.IsDeleted)
            .FirstOrDefaultAsync(l => l.ShortUrl == shortUrl);
        
        if (link == null)
            return Results.NotFound(new { Message = "The link was not found or is out of date." });
        
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();

        var click = new LinkClick
        {
            LinkId = link.Id,
            ClickedAt = DateTime.UtcNow,
            DeviceType = IdentifyDevice(httpContext),
            Source = IdentifySource(httpContext),
            Country = await IdentifyCountry(clientFactory, ipAddress),
        };

        db.LinkClicks.Add(click);
        await db.SaveChangesAsync();
        
        return Results.Redirect(link.OriginalUrl, true);
    }

    private static string IdentifyDevice(HttpContext httpContext)
    {
        var userAgent = httpContext.Request.Headers.UserAgent.ToString().ToLower();
        var deviceType = "Web / Desktop";

        if (userAgent.Contains("mobile") || userAgent.Contains("android") || userAgent.Contains("iphone"))
            deviceType = userAgent.Contains("iphone") || userAgent.Contains("ipad") ? "iOS" : "Android";
        
        return deviceType;
    }

    private static string IdentifySource(HttpContext httpContext)
    {
        var referer = httpContext.Request.Headers.Referer.ToString().ToLower();
        var source = "Direct click (or Mobile App)";

        if (!string.IsNullOrEmpty(referer))
        {
            if (referer.Contains("t.me") || referer.Contains("telegram")) 
                source = "Telegram";
            else if (referer.Contains("vk.com")) 
                source = "VK";
            else if (referer.Contains("facebook.com")) 
                source = "Facebook";
            else if (referer.Contains("instagram.com")) 
                source = "Instagram";
            else 
                source = new Uri(referer).Host;
        }

        return source;
    }

    private static async Task<string> IdentifyCountry(IHttpClientFactory factory, string? ipAddress)
    {
        var country = "Unknown";
        
        try
        {
            var client = factory.CreateClient();
            var response = await client.GetAsync($"http://ip-api.com/json/{ipAddress}?fields=status,country");
                    
            if (response.IsSuccessStatusCode)
            {
                var jsonString = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(jsonString);
                var root = doc.RootElement;
                        
                if (root.GetProperty("status").GetString() == "success")
                    country = root.GetProperty("country").GetString() ?? "Unknown";
            }
        }
        catch
        {
            country = "API Error";
        }

        return country;
    }
}