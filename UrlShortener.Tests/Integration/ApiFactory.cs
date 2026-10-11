using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using UrlShortener.Data;

namespace UrlShortener.Tests.Integration;

public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"urlshortener-test-{Guid.NewGuid():N}.db");
    protected virtual int PermitLimit => 100_000;

    // Tests move this clock forward to simulate time passing.
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("RateLimit:PermitLimit", PermitLimit.ToString());
        builder.ConfigureServices(services =>
        {
            // Swap the real database for a temporary one.
            services.RemoveAll<DbContextOptions<AppDb>>();
            services.AddDbContext<AppDb>(o => o.UseSqlite($"Data Source={_dbPath}"));

            // Swap the real clock for the fake one.
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });

    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;

        SqliteConnection.ClearAllPools();   // release the file lock
        try { File.Delete(_dbPath); } catch (IOException) { /* temp file, safe to leave */ }
    }
}