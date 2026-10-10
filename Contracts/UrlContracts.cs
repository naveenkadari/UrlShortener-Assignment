namespace UrlShortener.Contracts;

public record CreateUrlRequest(string? Url, string? CustomAlias, DateTimeOffset? ExpiresAt);

public record UrlResponse(
    string ShortCode,
    string ShortUrl,
    string OriginalUrl,
    DateTime CreatedAt,
    DateTime? ExpiresAt);