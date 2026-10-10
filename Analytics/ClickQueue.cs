using System.Threading.Channels;

namespace UrlShortener.Analytics;

public record ClickItem(string Code, DateTime At, string? Referrer, string? UserAgent);

public class ClickQueue
{
    private readonly Channel<ClickItem> _channel = Channel.CreateBounded<ClickItem>(
        new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropWrite });

    public bool TryEnqueue(ClickItem item) => _channel.Writer.TryWrite(item);

    public ChannelReader<ClickItem> Reader => _channel.Reader;
}