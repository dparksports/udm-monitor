using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace UdmMonitor.Core;

/// <summary>Shared state about the on-box watchdog, fed by the ntfy stream and read by the heartbeat watch.</summary>
public sealed class WatchdogState
{
    public DateTimeOffset? LastHeartbeat { get; set; }
    public bool IsConnected { get; set; }
    public DateTimeOffset? ConnectedSince { get; set; }
}

/// <summary>
/// Subscribes to the ntfy topic the on-box cam-watchdog.sh pushes to, over WebSocket.
/// Everything the UDM-side script detects (camera loss, evil twin, IPS events, WPA
/// drift, heartbeats) arrives here sub-second and is re-emitted on the bus.
/// </summary>
public sealed class NtfySubscriber : IAsyncDisposable
{
    private readonly string _topic;
    private readonly EventBus _bus;
    private readonly WatchdogState _state;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _task;

    public NtfySubscriber(MonitorConfig cfg, EventBus bus, WatchdogState state)
    {
        _topic = cfg.NtfyTopic;
        _bus = bus;
        _state = state;
        _task = Task.Run(() => RunAsync(_cts.Token));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var backoffSecs = 2;
        while (!ct.IsCancellationRequested)
        {
            using var ws = new ClientWebSocket();
            try
            {
                await ws.ConnectAsync(new Uri($"wss://ntfy.sh/{_topic}/ws"), ct);
                _state.IsConnected = true;
                _state.ConnectedSince = DateTimeOffset.UtcNow;
                // Avoid a false "watchdog silent" alarm at app start: the daily heartbeat
                // will overwrite this with its own timestamp when the next one lands.
                if (_state.LastHeartbeat is null) _state.LastHeartbeat = DateTimeOffset.UtcNow;
                backoffSecs = 2;
                _bus.Publish(new MonEvent(DateTimeOffset.UtcNow, EventSource.System, "ntfy", "connected",
                    $"subscribed to ntfy topic {_topic}", Severity.Info));
                await ReadLoopAsync(ws, ct);
                _bus.Publish(new MonEvent(DateTimeOffset.UtcNow, EventSource.System, "ntfy", "disconnected",
                    "ntfy stream closed by server, reconnecting", Severity.Default));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _bus.Publish(new MonEvent(DateTimeOffset.UtcNow, EventSource.System, "ntfy", "error",
                    $"ntfy connection error: {ex.Message}", Severity.Default));
            }
            finally
            {
                _state.IsConnected = false;
            }

            try { await Task.Delay(TimeSpan.FromSeconds(backoffSecs), ct); }
            catch (OperationCanceledException) { break; }
            backoffSecs = Math.Min(backoffSecs * 2, 60);
        }
    }

    private async Task ReadLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buf = new byte[64 * 1024];
        while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            var sb = new StringBuilder();
            WebSocketReceiveResult res;
            do
            {
                res = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                if (res.MessageType == WebSocketMessageType.Close) return;
                sb.Append(Encoding.UTF8.GetString(buf, 0, res.Count));
            } while (!res.EndOfMessage);

            try { HandleFrame(sb.ToString()); }
            catch { /* one malformed frame must not kill the subscription */ }
        }
    }

    private void HandleFrame(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("event", out var ev) || ev.GetString() != "message") return;

        var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
        var message = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
        var timeSec = root.TryGetProperty("time", out var tm) && tm.TryGetInt64(out var ts)
            ? ts
            : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // ntfy priorities: 5 max, 4 high, 3 default, 2 low, 1 min. The on-box script
        // sends "high"/"default"/"low" headers which map to 4/3/2.
        var sev = Severity.Default;
        if (root.TryGetProperty("priority", out var p) && p.TryGetInt32(out var pi))
            sev = pi >= 4 ? Severity.High : pi <= 2 ? Severity.Info : Severity.Default;

        if (title.Contains("heartbeat", StringComparison.OrdinalIgnoreCase))
        {
            _state.LastHeartbeat = DateTimeOffset.FromUnixTimeSeconds(timeSec);
            _bus.Publish(new MonEvent(DateTimeOffset.FromUnixTimeSeconds(timeSec), EventSource.Ntfy,
                title, "heartbeat", message, Severity.Info));
        }
        else
        {
            _bus.Publish(new MonEvent(DateTimeOffset.FromUnixTimeSeconds(timeSec), EventSource.Ntfy,
                title, "alert", message, sev));
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { await _task; } catch { }
        _cts.Dispose();
    }
}
