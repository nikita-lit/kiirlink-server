using KiirlinkServer.Models;
using Microsoft.EntityFrameworkCore;

namespace KiirlinkServer;

public static class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        
        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
                               ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        
        builder.Services.AddDbContext<DbContext>(options =>
            options.UseSqlite(connectionString));
        
        builder.Services.AddAuthentication();
        builder.Services.AddAuthorization();
        
        builder.Services.AddIdentityApiEndpoints<User>()
            .AddEntityFrameworkStores<DbContext>();
        
        builder.Services.AddOpenApi();

        var app = BuildApp(builder);
        app.Run();
    }

    private static WebApplication BuildApp(WebApplicationBuilder builder)
    {
        var app = builder.Build();

        if (app.Environment.IsDevelopment())
            app.MapOpenApi();

        app.UseHttpsRedirection();
        app.UseAuthorization();
        
        app.MapGroup("/auth")
            .MapIdentityApi<User>()
            .WithTags("Authentication");

        return app;
    }
}