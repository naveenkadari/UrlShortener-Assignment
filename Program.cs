using Microsoft.EntityFrameworkCore;
using UrlShortener.Contracts;
using UrlShortener.Data;
using UrlShortener.Services;
using UrlShortener.Domain;

var builder = WebApplication.CreateBuilder(args);

// --- Services (dependency injection registrations) ---
builder.Services.AddDbContext<AppDb>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=urls.db"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<UrlService>();
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
    TimeProvider clock,
    HttpContext http,
    CancellationToken ct) =>
{
    var url = await db.Urls.AsNoTracking().FirstOrDefaultAsync(u => u.Code == code, ct);

    if (url is null || url.IsDeleted)
        return Results.NotFound();

    var now = clock.GetUtcNow().UtcDateTime;

    if (url.ExpiresAt is not null && url.ExpiresAt <= now)
        return Results.StatusCode(StatusCodes.Status410Gone);

    // NAIVE: the visitor waits for this database write.
    db.Clicks.Add(new ClickEvent
    {
        Code = code,
        At = now,
        Referrer = Truncate(http.Request.Headers.Referer.ToString(), 2048),
        UserAgent = Truncate(http.Request.Headers.UserAgent.ToString(), 512)
    });
    await db.SaveChangesAsync(ct);

    return Results.Redirect(url.OriginalUrl, permanent: false);
});

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

app.Run();

static string? Truncate(string? value, int max) =>
    string.IsNullOrEmpty(value) ? null : value.Length <= max ? value : value[..max];

public partial class Program { }   // lets the integration tests (next step) start the app