using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UrlShortener.Data;

namespace UrlShortener.Tests.Integration;

public class ScenarioTests
{
    private static readonly WebApplicationFactoryClientOptions NoRedirect = new() { AllowAutoRedirect = false };

    [Fact]
    public async Task DefaultExpiry_IsApplied_ThenLinkExpires()
    {
        using var baseFactory = new ApiFactory();
        using var factory = baseFactory.WithWebHostBuilder(b => b.UseSetting("Links:DefaultExpiryDays", "7"));
        var client = factory.CreateClient(NoRedirect);

        var create = await client.PostAsJsonAsync("/api/v1/urls", new { url = "https://a.com" });
        var body = await create.Content.ReadFromJsonAsync<JsonElement>();
        var code = body.GetProperty("shortCode").GetString();

        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("expiresAt").ValueKind);
        Assert.Equal(HttpStatusCode.Found, (await client.GetAsync($"/{code}")).StatusCode);

        baseFactory.Clock.Advance(TimeSpan.FromDays(8));   // time passes, no sleeping

        Assert.Equal(HttpStatusCode.Gone, (await client.GetAsync($"/{code}")).StatusCode);
    }

    [Fact]
    public async Task Ready_WhenDatabaseWorks_Returns200()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient(NoRedirect);

        var res = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Ready_WhenDatabaseBroken_Returns503()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient(NoRedirect);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            await db.Database.ExecuteSqlRawAsync("DROP TABLE Urls");
        }

        var res = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
    }
}