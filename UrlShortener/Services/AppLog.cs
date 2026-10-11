namespace UrlShortener.Services;

internal static partial class AppLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Readiness check failed")]
    public static partial void ReadinessFailed(ILogger logger, Exception ex);
}