using System.Security.Claims;
using KiirlinkServer.Models;
using Microsoft.EntityFrameworkCore;

namespace KiirlinkServer.Endpoints;

public static partial class LinkEndpoints
{
    private static void MapFavouritesEndpoints( RouteGroupBuilder group )
    {
        group.MapGet( "/favourites", GetFavourites )
            .WithName( "GetFavourites" );

        group.MapPost( "/favourite", AddFavouriteLink )
            .WithName( "AddFavouriteLink" );

        group.MapPost( "/unfavourite", RemoveFavouriteLink )
            .WithName( "RemoveFavouriteLink" );
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
                f.Link.Id,
                LinkId = f.Link.Id,
                f.Link.ShortUrl,
                f.Link.OriginalUrl,
                f.Link.CreatedAt,
                f.Link.ExpiresAt,
                f.Link.IsPublic,
                Category = f.Link.Category == null ? null : f.Link.Category.Name,
                ClickCount = f.Link.LinkClicks.Count,
                IsFavourite = true,
                CategoryId = f.Link.CategoryId
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
}