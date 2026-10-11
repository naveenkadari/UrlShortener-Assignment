using Microsoft.Extensions.Hosting;
using UrlShortener.Data;
using UrlShortener.Domain;

namespace UrlShortener.Analytics;

public partial class ClickRecorder(ClickQueue queue, IServiceScopeFactory scopeFactory, ILogger<ClickRecorder> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var click in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDb>();

                db.Clicks.Add(new ClickEvent
                {
                    Code = click.Code,
                    At = click.At,
                    Referrer = click.Referrer,
                    UserAgent = click.UserAgent
                });
                await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A failed click is logged and skipped. It never reaches the visitor.
                LogClickFailed(logger, ex, click.Code);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to record click for {Code}")]
    private static partial void LogClickFailed(ILogger logger, Exception ex, string code);
}