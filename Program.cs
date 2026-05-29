using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using KiirlinkServer.Models;
using KiirlinkServer.Endpoints;
using Microsoft.AspNetCore.Identity;
using Microsoft.OpenApi;

namespace KiirlinkServer;

public static class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddHttpClient();
        
        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
                               ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        
        builder.Services.AddDbContext<DbContext>(options =>
            options.UseSqlite(connectionString));
        
        builder.Services.AddAuthentication();
        builder.Services.AddAuthorization();
        
        builder.Services.AddIdentityApiEndpoints<User>()
            .AddEntityFrameworkStores<DbContext>();
        
        builder.Services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
            
            options.AddOperationTransformer((operation, _, _) =>
            {
                operation.Security ??= [];
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference("Bearer")] = []
                });
                return Task.CompletedTask;
            });
        });
        
        var app = BuildApp(builder);
        app.MapLinkEndpoints();
        app.Run();
    }

    private static WebApplication BuildApp(WebApplicationBuilder builder)
    {
        var app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference(options =>
            {
                options.Authentication = new ScalarAuthenticationOptions
                {
                    PreferredSecuritySchemes = ["Bearer"]
                };
            });
        }

        app.UseHttpsRedirection();
        app.UseAuthorization();
        
        app.MapGroup("/auth")
            .MapIdentityApi<User>()
            .WithTags("Authentication");

        return app;
    }
}