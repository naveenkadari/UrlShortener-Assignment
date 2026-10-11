using Microsoft.EntityFrameworkCore;
using UrlShortener.Contracts;
using UrlShortener.Data;
using UrlShortener.Services;
using UrlShortener.Domain;
using UrlShortener.Analytics;
using Microsoft.Extensions.Caching.Memory;
using System.Threading.RateLimiting;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

// --- Services (dependency injection registrations) ---
builder.Services.AddDbContext<AppDb>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=urls.db"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<UrlService>();
builder.Services.AddSingleton<ClickQueue>();
builder.Services.Configure<UrlOptions>(builder.Configuration.GetSection("Links"));
builder.Services.AddHostedService<ClickRecorder>();
builder.Services.AddMemoryCache(options => options.SizeLimit = 10_000);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, token) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter =
    ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

        await context.HttpContext.Response.WriteAsJsonAsync(
            new { error = "too many requests, please retry later" }, token);
    };

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
    {
        // Health checks must never be throttled, or monitoring would report false outages.
        if (http.Request.Path.StartsWithSegments("/health"))
            return RateLimitPartition.GetNoLimiter("health");

        // Read lazily so tests can override the values.
        var permitLimit = builder.Configuration.GetValue("RateLimit:PermitLimit", 100);
        var windowSeconds = builder.Configuration.GetValue("RateLimit:WindowSeconds", 60);

        return RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromSeconds(windowSeconds),
                QueueLimit = 0          // reject immediately, never queue
            });
    });
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exception = context.Features
            .Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;

        if (exception is BadHttpRequestException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "request body is not valid JSON" });
        }
        else
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { error = "internal server error" });
        }
    });
});
app.UseRateLimiter();

// Create the database/tables on startup (prototype only; production would use migrations)
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDb>().Database.EnsureCreated();
}

app.UseSwagger();
app.UseSwaggerUI();

// --- Endpoints ---
app.MapGet("/health", () => Results.Ok(new { status = "UP" }));

app.MapPost("/api/v1/urls", async (
    CreateUrlRequest request,
    UrlService service,
    HttpContext http,
    CancellationToken ct) =>
{
    var result = await service.CreateAsync(
        request.Url, request.CustomAlias, request.ExpiresAt?.UtcDateTime, ct);

    return result.Status switch
    {
        CreateStatus.Invalid => Results.BadRequest(new { error = result.Error }),
        CreateStatus.Conflict => Results.Conflict(new { error = result.Error }),
        _ => Results.Created($"/{result.Url!.Code}", new UrlResponse(
            result.Url.Code,
            $"{http.Request.Scheme}://{http.Request.Host}/{result.Url.Code}",
            result.Url.OriginalUrl,
            result.Url.CreatedAt,
            result.Url.ExpiresAt))
    };
});

app.MapGet("/{code}", async (
    string code,
    AppDb db,
    IMemoryCache cache,
    TimeProvider clock,
    ClickQueue clickQueue,
    HttpContext http,
    CancellationToken ct) =>
{
    var cacheKey = $"url:{code}";

    if (!cache.TryGetValue(cacheKey, out ShortUrl? url))
    {
        url = await db.Urls.AsNoTracking().FirstOrDefaultAsync(u => u.Code == code, ct);

        if (url is not null)   // never cache "not found" results
        {
            cache.Set(cacheKey, url, new MemoryCacheEntryOptions
            {
                Size = 1,
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
            });
        }
    }

    if (url is null || url.IsDeleted)
        return Results.NotFound();

    var now = clock.GetUtcNow().UtcDateTime;

    if (url.ExpiresAt is not null && url.ExpiresAt <= now)
        return Results.StatusCode(StatusCodes.Status410Gone);

    clickQueue.TryEnqueue(new ClickItem(
        code,
        now,
        Truncate(http.Request.Headers.Referer.ToString(), 2048),
        Truncate(http.Request.Headers.UserAgent.ToString(), 512)));

    return Results.Redirect(url.OriginalUrl, permanent: false);
})
.WithName("Redirect")
.WithSummary("Redirects a short code to its original URL")
.WithDescription("Returns 302 with a Location header. Swagger UI cannot follow cross-origin redirects, so test this endpoint in a browser or with curl -i.")
.Produces(StatusCodes.Status302Found)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status410Gone)
.Produces(StatusCodes.Status429TooManyRequests);

app.MapGet("/api/v1/urls/{code}/stats", async (string code, AppDb db, CancellationToken ct) =>
{
    if (!await db.Urls.AnyAsync(u => u.Code == code, ct))
        return Results.NotFound();

    var clicks = db.Clicks.AsNoTracking().Where(c => c.Code == code);

    var total = await clicks.CountAsync(ct);
    var last = await clicks.MaxAsync(c => (DateTime?)c.At, ct);

    var byDay = await clicks
        .GroupBy(c => c.At.Date)
        .Select(g => new { date = g.Key, count = g.Count() })
        .OrderBy(x => x.date)
        .ToListAsync(ct);

    var referrers = await clicks
        .Where(c => c.Referrer != null)
        .GroupBy(c => c.Referrer)
        .Select(g => new { referrer = g.Key, count = g.Count() })
        .OrderByDescending(x => x.count)
        .Take(5)
        .ToListAsync(ct);

    return Results.Ok(new
    {
        shortCode = code,
        totalClicks = total,
        lastAccessedAt = last,
        clicksByDay = byDay,
        topReferrers = referrers
    });
});


app.MapDelete("/api/v1/urls/{code}", async (string code, AppDb db, IMemoryCache cache, CancellationToken ct) =>
{
    var url = await db.Urls.FirstOrDefaultAsync(u => u.Code == code && !u.IsDeleted, ct);

    if (url is null)
        return Results.NotFound();

    url.IsDeleted = true;
    await db.SaveChangesAsync(ct);
    cache.Remove($"url:{code}");   // otherwise the cache keeps serving the deleted link

    return Results.NoContent();
});

app.MapGet("/health/ready", async (AppDb db, ILogger<Program> logger, CancellationToken ct) =>
{
    try
    {
        await db.Urls.AsNoTracking().AnyAsync(ct);   // a real query, not just "can connect"
        return Results.Ok(new { status = "READY" });
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        AppLog.ReadinessFailed(logger, ex);
        return Results.Json(new { status = "NOT_READY" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.Run();

static string? Truncate(string? value, int max) =>
    string.IsNullOrEmpty(value) ? null : value.Length <= max ? value : value[..max];

public partial class Program { }   // lets the integration tests (next step) start the app