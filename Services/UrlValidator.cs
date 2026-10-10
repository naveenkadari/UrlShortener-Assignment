using System.Text.RegularExpressions;

namespace UrlShortener.Services;

public static partial class UrlValidator
{
    public const int MaxUrlLength = 2048;

    private static readonly HashSet<string> Reserved =
        new(StringComparer.OrdinalIgnoreCase) { "api", "health", "swagger" };

    [GeneratedRegex("^[A-Za-z0-9_-]{3,32}$")]
    private static partial Regex AliasRegex();

    public static bool TryValidateUrl(string? input, out string error)
    {
        error = "";

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "url is required";
            return false;
        }

        if (input.Length > MaxUrlLength)
        {
            error = "url too long";
            return false;
        }

        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            error = "url must be an absolute http/https URL";
            return false;
        }

        return true;
    }

    public static bool TryValidateAlias(string alias, out string error)
    {
        error = "";

        if (!AliasRegex().IsMatch(alias))
        {
            error = "alias must be 3-32 chars: letters, digits, '-' or '_'";
            return false;
        }

        if (Reserved.Contains(alias))
        {
            error = "alias is reserved";
            return false;
        }

        return true;
    }
}