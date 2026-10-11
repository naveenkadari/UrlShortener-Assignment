using Microsoft.EntityFrameworkCore;
using UrlShortener.Domain;

namespace UrlShortener.Data;

public class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<ShortUrl> Urls => Set<ShortUrl>();
    public DbSet<ClickEvent> Clicks => Set<ClickEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ShortUrl>().HasIndex(u => u.Code).IsUnique();   // collision guard
        modelBuilder.Entity<ShortUrl>().Property(u => u.Code).HasMaxLength(32);
        modelBuilder.Entity<ShortUrl>().Property(u => u.OriginalUrl).HasMaxLength(2048);
        modelBuilder.Entity<ClickEvent>().HasIndex(c => c.Code);
    }
}