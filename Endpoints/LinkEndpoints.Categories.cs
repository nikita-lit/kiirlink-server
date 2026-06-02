using System.Security.Claims;
using KiirlinkServer.Models;
using Microsoft.EntityFrameworkCore;

namespace KiirlinkServer.Endpoints;

public static partial class LinkEndpoints
{
    private static void MapCategoriesEndpoints( RouteGroupBuilder group )
    {
        group.MapGet( "/categories", GetCategories )
            .WithName( "GetCategories" );

        group.MapPost( "/category", AddCategory )
            .WithName( "AddCategory" );

        group.MapDelete( "/category/{id:int}", DeleteCategory )
            .WithName( "DeleteCategory" );

        group.MapPut( "/{id:int}/category", AssignCategory )
            .WithName( "AssignCategory" );
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
}