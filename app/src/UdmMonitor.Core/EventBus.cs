using System.Threading.Channels;

namespace UdmMonitor.Core;

/// <summary>
/// Fan-out event bus. Each subscriber gets its own bounded channel (drop-oldest under
/// pressure) so a slow UI never blocks the ingest pipeline and a dead logger can't
/// swallow events meant for the toasts.
/// </summary>
public sealed class EventBus
{
    private readonly object _lock = new();
    private readonly List<Channel<MonEvent>> _subscribers = new();

    public ChannelReader<MonEvent> Subscribe(int capacity = 4096)
    {
        var ch = Channel.CreateBounded<MonEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
        });
        lock (_lock) _subscribers.Add(ch);
        return ch.Reader;
    }

    public void Publish(MonEvent e)
    {
        lock (_lock)
        {
            foreach (var ch in _subscribers) ch.Writer.TryWrite(e);
        }
    }
}
