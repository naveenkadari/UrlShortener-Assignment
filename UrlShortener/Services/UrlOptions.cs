namespace UrlShortener.Services;

public class UrlOptions
{
    // null (the default) means links never expire unless the caller sets an expiry.
    public int? DefaultExpiryDays { get; set; }
}