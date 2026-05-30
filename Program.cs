using KiirlinkServer.Endpoints;
using KiirlinkServer.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace KiirlinkServer;

public static class Program
{
    public static void Main( string[] args )
    {
        var builder = WebApplication.CreateBuilder( args );
        builder.Services.AddHttpClient();

        var connectionString = builder.Configuration.GetConnectionString( "DefaultConnection" )
                               ?? throw new InvalidOperationException(
                                   "Connection string 'DefaultConnection' not found." );

        builder.Services.AddDbContext<DbContext>( options =>
            options.UseSqlite( connectionString ) );

        builder.Services.AddAuthentication();
        builder.Services.AddAuthorization();

        builder.Services.AddIdentityApiEndpoints<User>()
            .AddEntityFrameworkStores<DbContext>();

        builder.Services.AddOpenApi( options =>
        {
            options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();

            options.AddOperationTransformer( ( operation, _, _ ) =>
            {
                operation.Security ??= [];
                operation.Security.Add( new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference( "Bearer" )] = []
                } );
                return Task.CompletedTask;
            } );
        } );

        var app = BuildApp( builder );
        app.Run();
    }

    private static WebApplication BuildApp( WebApplicationBuilder builder )
    {
        var app = builder.Build();

        using ( var scope = app.Services.CreateScope() )
        {
            var db = scope.ServiceProvider.GetRequiredService<DbContext>();
            if ( db.Database.GetPendingMigrations().Any() )
                db.Database.Migrate();
        }

        if ( app.Environment.IsDevelopment() )
        {
            app.MapOpenApi();
            app.MapScalarApiReference( options =>
            {
                options.Authentication = new ScalarAuthenticationOptions
                {
                    PreferredSecuritySchemes = ["Bearer"]
                };
            } );
        }

        app.UseHttpsRedirection();
        app.UseAuthorization();

        app.MapGroup( "/api/auth" )
            .MapIdentityApi<User>()
            .WithTags( "Authentication" );

        app.MapLinkEndpoints();

        return app;
    }
}