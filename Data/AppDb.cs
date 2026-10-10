using Microsoft.EntityFrameworkCore;
using UrlShortener.Domain;

namespace UrlShortener.Data;

public class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<ShortUrl> Urls => Set<ShortUrl>();
    public DbSet<ClickEvent> Clicks => Set<ClickEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<ShortUrl>().HasIndex(u => u.Code).IsUnique();   // collision guard
        b.Entity<ShortUrl>().Property(u => u.Code).HasMaxLength(32);
        b.Entity<ShortUrl>().Property(u => u.OriginalUrl).HasMaxLength(2048);
        b.Entity<ClickEvent>().HasIndex(c => c.Code);
    }
}