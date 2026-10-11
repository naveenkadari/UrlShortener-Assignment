using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UrlShortener.Data;

namespace UrlShortener.Tests.Integration;

public class AnalyticsTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;

    public AnalyticsTests(ApiFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    private static async Task<string> CreateLinkAsync(HttpClient client)
    {
        var res = await client.PostAsJsonAsync("/api/v1/urls", new { url = "https://example.com" });
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("shortCode").GetString()!;
    }

    // Polls instead of asserting immediately, so the same test works
    // whether clicks are recorded synchronously or in the background.
    private static async Task<JsonElement> WaitForClicksAsync(HttpClient client, string code, int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        JsonElement stats;
        do
        {
            stats = await client.GetFromJsonAsync<JsonElement>($"/api/v1/urls/{code}/stats");
            if (stats.GetProperty("totalClicks").GetInt32() >= expected)
                return stats;
            await Task.Delay(50);
        } while (DateTime.UtcNow < deadline);

        return stats;
    }

    [Fact]
    public async Task Stats_UnknownCode_Returns404()
    {
        var res = await _client.GetAsync("/api/v1/urls/zzzzzzz/stats");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Stats_NewLink_ReturnsZeroClicks()
    {
        var code = await CreateLinkAsync(_client);

        var stats = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/urls/{code}/stats");

        Assert.Equal(0, stats.GetProperty("totalClicks").GetInt32());
        Assert.Equal(JsonValueKind.Null, stats.GetProperty("lastAccessedAt").ValueKind);
    }

    [Fact]
    public async Task Stats_AfterThreeRedirects_CountsThree()
    {
        var code = await CreateLinkAsync(_client);

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Found, (await _client.GetAsync($"/{code}")).StatusCode);

        var stats = await WaitForClicksAsync(_client, code, 3);

        Assert.Equal(3, stats.GetProperty("totalClicks").GetInt32());
        var perDayTotal = stats.GetProperty("clicksByDay").EnumerateArray()
            .Sum(d => d.GetProperty("count").GetInt32());
        Assert.Equal(3, perDayTotal);
    }

    [Fact]
    public async Task Stats_RecordsReferrer()
    {
        var code = await CreateLinkAsync(_client);
        var request = new HttpRequestMessage(HttpMethod.Get, $"/{code}");
        request.Headers.Referrer = new Uri("https://twitter.com/post/1");

        await _client.SendAsync(request);
        var stats = await WaitForClicksAsync(_client, code, 1);

        var top = stats.GetProperty("topReferrers")[0];
        Assert.Equal("https://twitter.com/post/1", top.GetProperty("referrer").GetString());
        Assert.Equal(1, top.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Redirect_StillWorks_WhenClickRecordingFails()
    {
        // Own factory, because this test deliberately breaks its database.
        using var factory = new ApiFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var code = await CreateLinkAsync(client);

        // Simulate an analytics outage: the Clicks table disappears. ??
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            await db.Database.ExecuteSqlRawAsync("DROP TABLE Clicks");
        }

        var res = await client.GetAsync($"/{code}");

        Assert.Equal(HttpStatusCode.Found, res.StatusCode);
    }
}