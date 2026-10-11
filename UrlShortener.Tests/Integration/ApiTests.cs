using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace UrlShortener.Tests.Integration;

public class ApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public ApiTests(ApiFactory factory)
    {
        _factory = factory;
        // Do NOT follow redirects, so we can inspect the 302 itself.
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    private static string Unique() => Guid.NewGuid().ToString("N")[..10];

    [Fact]
    public async Task Health_ReturnsUp()
    {
        var res = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("UP", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Create_ValidUrl_Returns201WithShortCode()
    {
        var res = await _client.PostAsJsonAsync("/api/v1/urls", new { url = "https://google.com" });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        var code = body.GetProperty("shortCode").GetString()!;
        Assert.Matches("^[0-9A-Za-z]{7}$", code);
        Assert.EndsWith("/" + code, body.GetProperty("shortUrl").GetString());
        Assert.Equal("https://google.com", body.GetProperty("originalUrl").GetString());
    }

    [Fact]
    public async Task Create_ThenRedirect_Returns302WithLocation()
    {
        var create = await _client.PostAsJsonAsync("/api/v1/urls", new { url = "https://example.com/path?x=1" });
        var code = (await create.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("shortCode").GetString();

        var redirect = await _client.GetAsync($"/{code}");

        Assert.Equal(HttpStatusCode.Found, redirect.StatusCode);   // 302, not 301
        Assert.Equal(new Uri("https://example.com/path?x=1"), redirect.Headers.Location);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    public async Task Create_InvalidUrl_Returns400(string url)
    {
        var res = await _client.PostAsJsonAsync("/api/v1/urls", new { url });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(body.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task Create_MissingUrl_Returns400UrlRequired()
    {
        var res = await _client.PostAsJsonAsync("/api/v1/urls", new { });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("url is required", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Create_MalformedJson_Returns400()
    {
        var content = new StringContent("{hello}", Encoding.UTF8, "application/json");

        var res = await _client.PostAsync("/api/v1/urls", content);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateAlias_Returns409()
    {
        var alias = "a" + Unique();
        var first = await _client.PostAsJsonAsync("/api/v1/urls", new { url = "https://a.com", customAlias = alias });
        var second = await _client.PostAsJsonAsync("/api/v1/urls", new { url = "https://b.com", customAlias = alias });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Redirect_UnknownCode_Returns404()
    {
        var res = await _client.GetAsync("/zzzzzzz");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Redirect_ExpiredLink_Returns410()
    {
        var expiresAt = _factory.Clock.GetUtcNow().AddMinutes(10);
        var create = await _client.PostAsJsonAsync("/api/v1/urls",
            new { url = "https://a.com", expiresAt });
        var code = (await create.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("shortCode").GetString();

        var before = await _client.GetAsync($"/{code}");
        _factory.Clock.Advance(TimeSpan.FromMinutes(11));   // time passes, no sleeping
        var after = await _client.GetAsync($"/{code}");

        Assert.Equal(HttpStatusCode.Found, before.StatusCode);
        Assert.Equal(HttpStatusCode.Gone, after.StatusCode);
    }

    [Fact]
    public async Task Create_50ConcurrentRequests_AllSucceedWithUniqueCodes()
    {
        var tasks = Enumerable.Range(0, 50)
            .Select(_ => _client.PostAsJsonAsync("/api/v1/urls", new { url = "https://a.com" }));

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var codes = new List<string>();
        foreach (var r in responses)
            codes.Add((await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("shortCode").GetString()!);
        Assert.Equal(50, codes.Distinct().Count());
    }

    private async Task<string> CreateLinkAsync()
    {
        var res = await _client.PostAsJsonAsync("/api/v1/urls", new { url = "https://example.com" });
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("shortCode").GetString()!;
    }

    [Fact]
    public async Task Delete_ThenRedirect_Returns404_EvenWhenPreviouslyCached()
    {
        var code = await CreateLinkAsync();

        // First redirect fills the cache.
        Assert.Equal(HttpStatusCode.Found, (await _client.GetAsync($"/{code}")).StatusCode);

        var delete = await _client.DeleteAsync($"/api/v1/urls/{code}");
        var after = await _client.GetAsync($"/{code}");

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
    }

    [Fact]
    public async Task Delete_UnknownCode_Returns404()
    {
        var res = await _client.DeleteAsync("/api/v1/urls/zzzzzzz");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Delete_Twice_SecondReturns404()
    {
        var code = await CreateLinkAsync();

        var first = await _client.DeleteAsync($"/api/v1/urls/{code}");
        var second = await _client.DeleteAsync($"/api/v1/urls/{code}");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
    }

    [Fact]
    public async Task Delete_KeepsStatsAvailable()
    {
        var code = await CreateLinkAsync();
        await _client.DeleteAsync($"/api/v1/urls/{code}");

        var stats = await _client.GetAsync($"/api/v1/urls/{code}/stats");

        Assert.Equal(HttpStatusCode.OK, stats.StatusCode);
    }
}