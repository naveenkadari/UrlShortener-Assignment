using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using UrlShortener.Data;
using UrlShortener.Services;

namespace UrlShortener.Tests.Unit;

public class UrlServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDb _db;
    private readonly FakeTimeProvider _clock;
    private readonly UrlService _service;

    public UrlServiceTests()
    {
        // In-memory SQLite lives only as long as the connection stays open.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDb>().UseSqlite(_connection).Options;
        _db = new AppDb(options);
        _db.Database.EnsureCreated();

        _clock = new FakeTimeProvider();
        _clock.SetUtcNow(new DateTimeOffset(2026, 10, 10, 10, 0, 0, TimeSpan.Zero));

        _service = new UrlService(_db, _clock);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task Create_ValidUrl_ReturnsCreatedWithGeneratedCode()
    {
        var result = await _service.CreateAsync("https://google.com", null, null, default);

        Assert.Equal(CreateStatus.Created, result.Status);
        Assert.Matches("^[0-9A-Za-z]{7}$", result.Url!.Code);
        Assert.Equal("https://google.com", result.Url.OriginalUrl);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, result.Url.CreatedAt);
    }

    [Fact]
    public async Task Create_InvalidUrl_ReturnsInvalidAndSavesNothing()
    {
        var result = await _service.CreateAsync("javascript:alert(1)", null, null, default);

        Assert.Equal(CreateStatus.Invalid, result.Status);
        Assert.NotNull(result.Error);
        Assert.Equal(0, await _db.Urls.CountAsync());
    }

    [Fact]
    public async Task Create_CustomAlias_UsesAliasAsCode()
    {
        var result = await _service.CreateAsync("https://a.com", "promo", null, default);

        Assert.Equal(CreateStatus.Created, result.Status);
        Assert.Equal("promo", result.Url!.Code);
    }

    [Fact]
    public async Task Create_DuplicateAlias_ReturnsConflict()
    {
        await _service.CreateAsync("https://a.com", "promo", null, default);

        var result = await _service.CreateAsync("https://b.com", "promo", null, default);

        Assert.Equal(CreateStatus.Conflict, result.Status);
        Assert.Equal(1, await _db.Urls.CountAsync());
    }

    [Fact]
    public async Task Create_InvalidAlias_ReturnsInvalid()
    {
        var result = await _service.CreateAsync("https://a.com", "a b/c", null, default);

        Assert.Equal(CreateStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task Create_ExpiryInThePast_ReturnsInvalid()
    {
        var past = _clock.GetUtcNow().UtcDateTime.AddMinutes(-1);

        var result = await _service.CreateAsync("https://a.com", null, past, default);

        Assert.Equal(CreateStatus.Invalid, result.Status);
        Assert.Equal("expiresAt must be in the future", result.Error);
    }

    [Fact]
    public async Task Create_ExpiryInTheFuture_IsStored()
    {
        var future = _clock.GetUtcNow().UtcDateTime.AddDays(1);

        var result = await _service.CreateAsync("https://a.com", null, future, default);

        Assert.Equal(CreateStatus.Created, result.Status);
        Assert.Equal(future, result.Url!.ExpiresAt);
    }
}