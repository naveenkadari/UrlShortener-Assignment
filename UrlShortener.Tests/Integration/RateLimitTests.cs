using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace UrlShortener.Tests.Integration;

// Same app, but allowing only 3 requests per window.
public class RateLimitedFactory : ApiFactory
{
    protected override int PermitLimit => 3;
}

public class RateLimitTests : IClassFixture<RateLimitedFactory>
{
    private readonly HttpClient _client;

    public RateLimitTests(RateLimitedFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task ExceedingLimit_Returns429WithRetryAfter()
    {
        for (var i = 0; i < 3; i++)
        {
            var ok = await _client.GetAsync("/zzzzzzz");   // 404, but it still counts as a request
            Assert.Equal(HttpStatusCode.NotFound, ok.StatusCode);
        }

        var limited = await _client.GetAsync("/zzzzzzz");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"));
        var body = await limited.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("too many requests, please retry later", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Health_IsNeverRateLimited()
    {
        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/health")).StatusCode);
    }
}