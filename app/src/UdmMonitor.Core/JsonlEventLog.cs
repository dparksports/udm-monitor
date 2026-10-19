using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace UdmMonitor.Core;

/// <summary>
/// Durable append-only event log (one JSON object per line) with a single-generation
/// rotation. This replaces hand-rolled Add-Content loops like netmon.log with structured,
/// queryable history; SQLite is the phase-2 upgrade behind the same role.
/// </summary>
public sealed class JsonlEventLog : IAsyncDisposable
{
    private static readonly JsonSerializerOptions SerOpts = new();

    private readonly string _path;
    private readonly long _maxBytes;
    private readonly ChannelReader<MonEvent> _reader;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _task;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public JsonlEventLog(EventBus bus, string directory, long maxBytes = 5 * 1024 * 1024)
    {
        _maxBytes = maxBytes;
        _reader = bus.Subscribe();
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "events.jsonl");
        _task = Task.Run(() => RunAsync(_cts.Token));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        await foreach (var e in _reader.ReadAllAsync(ct))
        {
            try
            {
                var line = JsonSerializer.Serialize(e, SerOpts) + "\n";
                await _fileLock.WaitAsync(ct);
                try
                {
                    var fi = new FileInfo(_path);
                    if (fi.Exists && fi.Length > _maxBytes)
                        File.Move(_path, _path + ".1", overwrite: true);
                    await File.AppendAllTextAsync(_path, line, Encoding.UTF8, ct);
                }
                finally
                {
                    _fileLock.Release();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // a logging failure must never take down the monitoring pipeline
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { await _task; } catch { }
        _fileLock.Dispose();
        _cts.Dispose();
    }
}
