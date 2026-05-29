using System.Security.Claims;
using System.Text.Json;
using KiirlinkServer.Models;
using Microsoft.EntityFrameworkCore;

namespace KiirlinkServer.Endpoints;

public static class LinkEndpoints
{
    private record ShortenRequest(string LongUrl);
    
    public static void MapLinkEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/links");

        group.MapPost("/shorten", async (ShortenRequest request, DbContext db, ClaimsPrincipal user) =>
            {
                var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId)) 
                    return Results.Unauthorized();

                var shortUrl = Guid.NewGuid().ToString()[..6];
                var newLink = new Link
                {
                    OriginalUrl = request.LongUrl,
                    ShortUrl = shortUrl,
                    UserId = userId,
                    CreatedAt = DateTime.UtcNow,
                };

                db.Links.Add(newLink);
                await db.SaveChangesAsync();

                return Results.Created($"/api/links/{shortUrl}", new { newLink.ShortUrl, newLink.OriginalUrl });
            })
            .RequireAuthorization()
            .WithName("CreateShortUrl");
        
        app.MapGet("/{shortUrl}", async (
                string shortUrl, 
                DbContext db, 
                HttpContext httpContext,
                IHttpClientFactory clientFactory) =>
            {
                var link = await db.Links
                    .FirstOrDefaultAsync(l => l.ShortUrl == shortUrl);
                
                if (link == null)
                    return Results.NotFound(new { Message = "The link was not found or is out of date." });

                var userAgent = httpContext.Request.Headers.UserAgent.ToString().ToLower();
                var deviceType = "Web / Desktop";
    
                if (userAgent.Contains("mobile") || userAgent.Contains("android") || userAgent.Contains("iphone"))
                    deviceType = userAgent.Contains("iphone") || userAgent.Contains("ipad") ? "iOS" : "Android";
                
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
                
                var country = "Unknown";
                var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
                
                try
                {
                    var client = clientFactory.CreateClient();
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
                
                var click = new LinkClick
                {
                    LinkId = link.Id,
                    ClickedAt = DateTime.UtcNow,
                    DeviceType = deviceType,
                    Source = source,
                    Country = country,
                };

                db.LinkClicks.Add(click);
                await db.SaveChangesAsync();
                
                return Results.Redirect(link.OriginalUrl, permanent: false);
            })
            .WithName("RedirectToOriginalUrl");
    }
}