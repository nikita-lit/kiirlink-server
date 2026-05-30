using System.Security.Claims;
using System.Text.Json;
using KiirlinkServer.Models;
using Microsoft.EntityFrameworkCore;

namespace KiirlinkServer.Endpoints;

public static class LinkEndpoints
{
    public static void MapLinkEndpoints( this IEndpointRouteBuilder app )
    {
        var group = app.MapGroup( "/api/links" )
            .RequireAuthorization()
            .WithTags( "Links" );

        group.MapPost( "/shorten", ShortenLongUrl )
            .WithName( "CreateShortUrl" );

        group.MapGet( "/get", GetUrls )
            .WithName( "GetUrls" );

        group.MapPost( "/remove", RemoveShortUrl )
            .WithName( "RemoveShortUrl" );

        group.MapGet( "/{id:int}/stats", GetLinkStats )
            .WithName( "GetLinkStats" );

        // Favourites
        group.MapGet( "/favourites", GetFavourites )
            .WithName( "GetFavourites" );

        group.MapPost( "/favourite", AddFavouriteLink )
            .WithName( "AddFavouriteLink" );

        group.MapPost( "/unfavourite", RemoveFavouriteLink )
            .WithName( "RemoveFavouriteLink" );

        // Categories
        group.MapGet( "/categories", GetCategories )
            .WithName( "GetCategories" );

        group.MapPost( "/category", AddCategory )
            .WithName( "AddCategory" );

        group.MapDelete( "/category/{id:int}", DeleteCategory )
            .WithName( "DeleteCategory" );

        group.MapPut( "/{id:int}/category", AssignCategory )
            .WithName( "AssignCategory" );

        app.MapGet( "/{shortUrl}", RedirectToOriginalUrl )
            .WithName( "RedirectToOriginalUrl" )
            .WithTags( "Links" );
    }

    private static async Task<IResult> ShortenLongUrl(
        string longUrl,
        DbContext db,
        ClaimsPrincipal user )
    {
        var userId = user.FindFirst( ClaimTypes.NameIdentifier )?.Value;
        if ( string.IsNullOrEmpty( userId ) )
            return Results.Unauthorized();

        var shortUrl = Guid.NewGuid().ToString()[..6];
        var newLink = new Link
        {
            OriginalUrl = longUrl,
            ShortUrl = shortUrl,
            UserId = userId,
            CreatedAt = DateTime.UtcNow
        };

        db.Links.Add( newLink );
        await db.SaveChangesAsync();

        db.ActivityLogs.Add( new ActivityLog
        {
            LinkId = newLink.Id,
            Action =
                $"The user {user.Identity?.Name} created the link {newLink.ShortUrl}, which leads to {newLink.OriginalUrl}",
            CreatedAt = DateTime.UtcNow
        } );
        await db.SaveChangesAsync();

        return Results.Created( $"/api/links/{shortUrl}",
            new { newLink.ShortUrl, newLink.OriginalUrl, newLink.CreatedAt } );
    }

    private static async Task<IResult> GetUrls(
        DbContext db,
        ClaimsPrincipal user )
    {
        var userId = user.FindFirst( ClaimTypes.NameIdentifier )?.Value;
        if ( string.IsNullOrEmpty( userId ) )
            return Results.Unauthorized();

        var links = await db.Links
            .Where( l => !l.IsDeleted )
            .Where( l => l.UserId == userId )
            .Select( l => new
            {
                l.Id,
                l.OriginalUrl,
                l.ShortUrl,
                l.CreatedAt,
                Category = l.Category == null ? null : l.Category.Name,
                ClickCount = l.LinkClicks.Count
            } )
            .ToListAsync();

        return Results.Ok( links );
    }

    private static async Task<IResult> RemoveShortUrl(
        int linkId,
        DbContext db,
        ClaimsPrincipal user )
    {
        var userId = user.FindFirst( ClaimTypes.NameIdentifier )?.Value;
        if ( string.IsNullOrEmpty( userId ) )
            return Results.Unauthorized();

        var link = await db.Links
            .FirstOrDefaultAsync( l => l.Id == linkId && l.UserId == userId && !l.IsDeleted );

        if ( link == null )
            return Results.NotFound( new { Message = "The link was not found or is out of date." } );

        link.IsDeleted = true;

        db.ActivityLogs.Add( new ActivityLog
        {
            LinkId = link.Id,
            Action =
                $"The user {user.Identity?.Name} deleted the link {link.ShortUrl}, which led to {link.OriginalUrl}",
            CreatedAt = DateTime.UtcNow
        } );

        await db.SaveChangesAsync();

        return Results.Ok( new { Message = "Link successfully deleted." } );
    }

    private static async Task<IResult> GetLinkStats(
        int id,
        DbContext db,
        ClaimsPrincipal user )
    {
        var userId = user.FindFirst( ClaimTypes.NameIdentifier )?.Value;
        if ( string.IsNullOrEmpty( userId ) )
            return Results.Unauthorized();

        var link = await db.Links
            .Include( l => l.LinkClicks )
            .FirstOrDefaultAsync( l => l.Id == id && l.UserId == userId && !l.IsDeleted );

        if ( link == null )
            return Results.NotFound( new { Message = "The link was not found or is out of date." } );

        var stats = new
        {
            link.Id,
            link.ShortUrl,
            link.OriginalUrl,
            link.CreatedAt,
            TotalClicks = link.LinkClicks.Count,
            ByDevice = link.LinkClicks
                .GroupBy( c => c.DeviceType )
                .Select( g => new { Device = g.Key, Count = g.Count() } ),
            BySource = link.LinkClicks
                .GroupBy( c => c.Source )
                .Select( g => new { Source = g.Key, Count = g.Count() } ),
            ByCountry = link.LinkClicks
                .GroupBy( c => c.Country )
                .Select( g => new { Country = g.Key, Count = g.Count() } )
        };

        return Results.Ok( stats );
    }

    private static async Task<IResult> GetFavourites(
        DbContext db,
        ClaimsPrincipal user )
    {
        var userId = user.FindFirst( ClaimTypes.NameIdentifier )?.Value;
        if ( string.IsNullOrEmpty( userId ) )
            return Results.Unauthorized();

        var favourites = await db.Favourites
            .Where( f => f.UserId == userId )
            .Where( f => !f.Link.IsDeleted )
            .Select( f => new
            {
                f.Id,
                f.LinkId,
                f.Link.ShortUrl,
                f.Link.OriginalUrl,
                f.CreatedAt
            } )
            .ToListAsync();

        return Results.Ok( favourites );
    }

    private static async Task<IResult> AddFavouriteLink(
        int linkId,
        DbContext db,
        ClaimsPrincipal user )
    {
        var userId = user.FindFirst( ClaimTypes.NameIdentifier )?.Value;
        if ( string.IsNullOrEmpty( userId ) )
            return Results.Unauthorized();

        var link = await db.Links
            .FirstOrDefaultAsync( l => l.Id == linkId && l.UserId == userId );

        if ( link == null )
            return Results.NotFound( new { Message = "The link was not found or is out of date." } );

        var exists = await db.Favourites.AnyAsync( f => f.LinkId == link.Id && f.UserId == userId );
        if ( exists )
            return Results.BadRequest( new { Message = "Link already in favourites." } );

        var fav = new Favourite
        {
            LinkId = link.Id,
            UserId = userId,
            CreatedAt = DateTime.UtcNow
        };

        db.Favourites.Add( fav );
        await db.SaveChangesAsync();

        db.ActivityLogs.Add( new ActivityLog
        {
            LinkId = link.Id,
            Action = $"The user {user.Identity?.Name} added the link {link.ShortUrl} to favourites",
            CreatedAt = DateTime.UtcNow
        } );
        await db.SaveChangesAsync();

        return Results.Ok( new { Message = "Link added to favourites." } );
    }

    private static async Task<IResult> RemoveFavouriteLink(
        int linkId,
        DbContext db,
        ClaimsPrincipal user )
    {
        var userId = user.FindFirst( ClaimTypes.NameIdentifier )?.Value;
        if ( string.IsNullOrEmpty( userId ) )
            return Results.Unauthorized();

        var fav = await db.Favourites
            .FirstOrDefaultAsync( f => f.LinkId == linkId && f.UserId == userId );

        if ( fav == null )
            return Results.NotFound( new { Message = "The link is not in favourites." } );

        var link = await db.Links.FindAsync( fav.LinkId );

        db.Favourites.Remove( fav );

        db.ActivityLogs.Add( new ActivityLog
        {
            LinkId = fav.LinkId,
            Action =
                $"The user {user.Identity?.Name} removed the link {link?.ShortUrl ?? fav.LinkId.ToString()} from favourites",
            CreatedAt = DateTime.UtcNow
        } );
        await db.SaveChangesAsync();

        return Results.Ok( new { Message = "Link removed from favourites." } );
    }

    private static async Task<IResult> GetCategories( DbContext db )
    {
        var categories = await db.Categories
            .Select( c => new { c.Id, c.Name, LinkCount = c.Links.Count( l => !l.IsDeleted ) } )
            .ToListAsync();

        return Results.Ok( categories );
    }

    private static async Task<IResult> AddCategory(
        string categoryName,
        DbContext db,
        ClaimsPrincipal user )
    {
        var userId = user.FindFirst( ClaimTypes.NameIdentifier )?.Value;
        if ( string.IsNullOrEmpty( userId ) )
            return Results.Unauthorized();

        var exists = await db.Categories.AnyAsync( f => f.Name.ToLower() == categoryName.ToLower() );

        if ( exists )
            return Results.BadRequest( new { Message = "Category already exists." } );

        var cat = new Category
        {
            Name = categoryName
        };

        db.Categories.Add( cat );
        await db.SaveChangesAsync();

        db.ActivityLogs.Add( new ActivityLog
        {
            LinkId = null,
            Action = $"The user {user.Identity?.Name} created the category '{cat.Name}'",
            CreatedAt = DateTime.UtcNow
        } );
        await db.SaveChangesAsync();

        return Results.Created( "/api/links/categories", new { cat.Id, cat.Name } );
    }

    private static async Task<IResult> DeleteCategory(
        int id,
        DbContext db,
        ClaimsPrincipal user )
    {
        var userId = user.FindFirst( ClaimTypes.NameIdentifier )?.Value;
        if ( string.IsNullOrEmpty( userId ) )
            return Results.Unauthorized();

        var category = await db.Categories.FindAsync( id );
        if ( category == null )
            return Results.NotFound( new { Message = "Category not found." } );

        await db.Links
            .Where( l => l.CategoryId == id )
            .ExecuteUpdateAsync( s => s.SetProperty( l => l.CategoryId, (int?)null ) );

        db.Categories.Remove( category );

        db.ActivityLogs.Add( new ActivityLog
        {
            LinkId = null,
            Action = $"The user {user.Identity?.Name} deleted the category '{category.Name}'",
            CreatedAt = DateTime.UtcNow
        } );
        await db.SaveChangesAsync();

        return Results.Ok( new { Message = "Category deleted." } );
    }

    private static async Task<IResult> AssignCategory(
        int id,
        int? categoryId,
        DbContext db,
        ClaimsPrincipal user )
    {
        var userId = user.FindFirst( ClaimTypes.NameIdentifier )?.Value;
        if ( string.IsNullOrEmpty( userId ) )
            return Results.Unauthorized();

        var link = await db.Links
            .FirstOrDefaultAsync( l => l.Id == id && l.UserId == userId && !l.IsDeleted );

        if ( link == null )
            return Results.NotFound( new { Message = "The link was not found or is out of date." } );

        if ( categoryId.HasValue )
        {
            var categoryExists = await db.Categories.AnyAsync( c => c.Id == categoryId.Value );
            if ( !categoryExists )
                return Results.NotFound( new { Message = "Category not found." } );
        }

        var oldCategoryId = link.CategoryId;
        link.CategoryId = categoryId;

        var actionText = categoryId.HasValue
            ? $"The user {user.Identity?.Name} assigned category #{categoryId} to link {link.ShortUrl}"
            : $"The user {user.Identity?.Name} removed category from link {link.ShortUrl} (was #{oldCategoryId})";

        db.ActivityLogs.Add( new ActivityLog
        {
            LinkId = link.Id,
            Action = actionText,
            CreatedAt = DateTime.UtcNow
        } );
        await db.SaveChangesAsync();

        return Results.Ok( new { Message = "Category assigned.", link.Id, link.CategoryId } );
    }

    private static async Task<IResult> RedirectToOriginalUrl(
        string shortUrl,
        DbContext db,
        HttpContext httpContext,
        IHttpClientFactory clientFactory )
    {
        var link = await db.Links
            .Where( l => !l.IsDeleted )
            .FirstOrDefaultAsync( l => l.ShortUrl == shortUrl );

        if ( link == null )
            return Results.NotFound( new { Message = "The link was not found or is out of date." } );

        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();

        var click = new LinkClick
        {
            LinkId = link.Id,
            ClickedAt = DateTime.UtcNow,
            DeviceType = IdentifyDevice( httpContext ),
            Source = IdentifySource( httpContext ),
            Country = await IdentifyCountry( clientFactory, ipAddress )
        };

        db.LinkClicks.Add( click );
        await db.SaveChangesAsync();

        return Results.Redirect( link.OriginalUrl, true );
    }

    private static string IdentifyDevice( HttpContext httpContext )
    {
        var userAgent = httpContext.Request.Headers.UserAgent.ToString().ToLower();
        var deviceType = "Web / Desktop";

        if ( userAgent.Contains( "mobile" ) || userAgent.Contains( "android" ) || userAgent.Contains( "iphone" ) )
            deviceType = userAgent.Contains( "iphone" ) || userAgent.Contains( "ipad" ) ? "iOS" : "Android";

        return deviceType;
    }

    private static string IdentifySource( HttpContext httpContext )
    {
        var referer = httpContext.Request.Headers.Referer.ToString().ToLower();
        var source = "Direct click (or Mobile App)";

        if ( !string.IsNullOrEmpty( referer ) )
        {
            if ( referer.Contains( "t.me" ) || referer.Contains( "telegram" ) )
                source = "Telegram";
            else if ( referer.Contains( "facebook.com" ) )
                source = "Facebook";
            else if ( referer.Contains( "instagram.com" ) )
                source = "Instagram";
            else
                source = new Uri( referer ).Host;
        }

        return source;
    }

    private static async Task<string> IdentifyCountry( IHttpClientFactory factory, string? ipAddress )
    {
        var country = "Unknown";

        try
        {
            var client = factory.CreateClient();
            var response = await client.GetAsync( $"http://ip-api.com/json/{ipAddress}?fields=status,country" );

            if ( response.IsSuccessStatusCode )
            {
                var jsonString = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse( jsonString );
                var root = doc.RootElement;

                if ( root.GetProperty( "status" ).GetString() == "success" )
                    country = root.GetProperty( "country" ).GetString() ?? "Unknown";
            }
        }
        catch
        {
            country = "Unknown";
        }

        return country;
    }
}