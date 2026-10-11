namespace UrlShortener.Domain
{
    public class ShortUrl
    {
        public long Id { get; set; }
        public string Code { get; set; } = default!;
        public string OriginalUrl { get; set; } = default!;
        public DateTime CreatedAt { get; set; }      // UTC
        public DateTime? ExpiresAt { get; set; }     // UTC
        public bool IsDeleted { get; set; }
    }
}
