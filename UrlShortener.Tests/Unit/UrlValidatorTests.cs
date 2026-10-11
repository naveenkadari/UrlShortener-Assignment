using UrlShortener.Services;

namespace UrlShortener.Tests.Unit;

public class UrlValidatorTests
{
    [Theory]
    [InlineData("https://google.com", true)]
    [InlineData("http://a.com/x?y=1", true)]
    [InlineData("not-a-url", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("ftp://x.com", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ValidateUrl(string? input, bool expected) =>
        Assert.Equal(expected, UrlValidator.TryValidateUrl(input, out _));

    [Fact]
    public void RejectsOverlongUrl() =>
        Assert.False(UrlValidator.TryValidateUrl("https://a.com/" + new string('x', 3000), out _));

    [Fact]
    public void ReturnsErrorMessageWhenInvalid()
    {
        UrlValidator.TryValidateUrl("", out var error);
        Assert.Equal("url is required", error);
    }

    [Theory]
    [InlineData("promo", true)]
    [InlineData("my-link_1", true)]
    [InlineData("a b/c", false)]
    [InlineData("ab", false)]
    [InlineData("api", false)]
    [InlineData("API", false)]
    public void ValidateAlias(string alias, bool expected) =>
        Assert.Equal(expected, UrlValidator.TryValidateAlias(alias, out _));
}