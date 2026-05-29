using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using KiirlinkServer.Models;

namespace KiirlinkServer;

public class DbContext(DbContextOptions<DbContext> options) : IdentityDbContext<User>(options)
{
    public DbSet<Link> Links { get; set; }
    public DbSet<Category> Categories { get; set; }
    public DbSet<Favourite> Favourites { get; set; }
    public DbSet<LinkClick> LinkClicks { get; set; }
    public DbSet<ActivityLog> ActivityLogs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        modelBuilder.Entity<Link>()
            .HasOne(l => l.User)
            .WithMany(u => u.Links)
            .HasForeignKey(l => l.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        
        modelBuilder.Entity<Favourite>()
            .HasOne(f => f.User)
            .WithMany(u => u.Favourites)
            .HasForeignKey(f => f.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        
        modelBuilder.Entity<Favourite>()
            .HasOne(f => f.Link)
            .WithMany(l => l.Favourites)
            .HasForeignKey(f => f.LinkId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}