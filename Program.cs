using Microsoft.EntityFrameworkCore;
using UrlShortener.Contracts;
using UrlShortener.Data;
using UrlShortener.Services;

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
    CancellationToken ct) =>
{
    var url = await db.Urls.AsNoTracking().FirstOrDefaultAsync(u => u.Code == code, ct);

    if (url is null || url.IsDeleted)
        return Results.NotFound();

    if (url.ExpiresAt is not null && url.ExpiresAt <= clock.GetUtcNow().UtcDateTime)
        return Results.StatusCode(StatusCodes.Status410Gone);

    return Results.Redirect(url.OriginalUrl, permanent: false);   // 302
});

app.Run();

public partial class Program { }   // lets the integration tests (next step) start the app