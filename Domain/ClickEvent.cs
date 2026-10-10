namespace UrlShortener.Domain
{
    public class ClickEvent
    {
        public long Id { get; set; }
        public string Code { get; set; } = default!;
        public DateTime At { get; set; }
        public string? Referrer { get; set; }
        public string? UserAgent { get; set; }
    }
}
