namespace UdmMonitor.Core;

/// <summary>
/// Watch-the-watcher: the on-box watchdog heartbeats to ntfy once a day. If that
/// heartbeat stops arriving, the script on the UDM is dead and nothing on the UDM
/// can tell us — only this side can. Re-alerts hourly while it stays silent.
/// </summary>
public sealed class HeartbeatWatch : IAsyncDisposable
{
    private readonly MonitorConfig _cfg;
    private readonly EventBus _bus;
    private readonly WatchdogState _state;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _task;
    private long _lastAlertUnix;

    public HeartbeatWatch(MonitorConfig cfg, EventBus bus, WatchdogState state)
    {
        _cfg = cfg;
        _bus = bus;
        _state = state;
        _task = Task.Run(() => RunAsync(_cts.Token));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromMinutes(5), ct); }
            catch (OperationCanceledException) { break; }

            if (_state.LastHeartbeat is not { } hb) continue;
            var now = DateTimeOffset.UtcNow;
            var silentSecs = (long)(now - hb).TotalSeconds;
            if (silentSecs > _cfg.HeartbeatTimeoutSec && now.ToUnixTimeSeconds() - _lastAlertUnix > 3600)
            {
                _lastAlertUnix = now.ToUnixTimeSeconds();
                _bus.Publish(new MonEvent(now, EventSource.HeartbeatWatch, "watchdog", "silent",
                    $"No on-box watchdog heartbeat for {silentSecs / 3600.0:F1} h — cam-watchdog on the UDM may be dead",
                    Severity.High));
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { await _task; } catch { }
        _cts.Dispose();
    }
}
