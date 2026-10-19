using System.Net.NetworkInformation;

namespace UdmMonitor.Core;

public enum TargetState { Init, Up, Down }

public sealed class TargetStatus
{
    public string Name { get; init; } = "";
    public string Address { get; init; } = "";
    public TargetState State { get; internal set; } = TargetState.Init;
    public DateTimeOffset? DownSince { get; internal set; }
    public DateTimeOffset? LastSeen { get; internal set; }
    public long LastRttMs { get; internal set; } = -1;
}

/// <summary>
/// Out-of-band liveness from this PC. This is the check the on-box watchdog can never
/// run for itself: if the UDM Pro is the thing that died, this side still sees it.
/// Debounce mirrors cam_check(): N consecutive misses before declaring down, then
/// cooldown-gated re-alerts instead of a notification storm.
/// </summary>
public sealed class IcmpProber : IAsyncDisposable
{
    private readonly MonitorConfig _cfg;
    private readonly EventBus _bus;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _task;
    private readonly List<TargetStatus> _targets;
    private readonly int[] _misses;
    private readonly long[] _realertAtUnix; // earliest unix-second at which a still-down re-alert may fire

    public IReadOnlyList<TargetStatus> Targets => _targets;

    public IcmpProber(MonitorConfig cfg, EventBus bus)
    {
        _cfg = cfg;
        _bus = bus;
        _targets = cfg.Targets.Select(t => new TargetStatus { Name = t.Name, Address = t.Address }).ToList();
        _misses = new int[_targets.Count];
        _realertAtUnix = new long[_targets.Count];
        _task = Task.Run(() => RunAsync(_cts.Token));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var ping = new Ping();
        while (!ct.IsCancellationRequested)
        {
            for (var i = 0; i < _targets.Count; i++)
            {
                if (ct.IsCancellationRequested) break;
                var t = _targets[i];

                var ok = false;
                long rtt = -1;
                try
                {
                    var r = await ping.SendPingAsync(t.Address, _cfg.ProbeTimeoutMs);
                    ok = r.Status == IPStatus.Success;
                    if (ok) rtt = r.RoundtripTime;
                }
                catch
                {
                    ok = false; // Ping throws on unreachable network, DNS failure, etc.
                }

                var now = DateTimeOffset.UtcNow;
                if (ok)
                {
                    t.LastSeen = now;
                    t.LastRttMs = rtt;
                    if (t.State != TargetState.Up)
                    {
                        var wasDown = t.State == TargetState.Down;
                        t.State = TargetState.Up;
                        _bus.Publish(new MonEvent(now, EventSource.Icmp, t.Name,
                            wasDown ? "up" : "up-initial",
                            wasDown
                                ? $"{t.Name} ({t.Address}) reachable again, rtt {rtt} ms"
                                : $"{t.Name} ({t.Address}) is up, rtt {rtt} ms",
                            wasDown ? Severity.Default : Severity.Info));
                    }
                    _misses[i] = 0;
                }
                else
                {
                    _misses[i]++;
                    if (_misses[i] >= _cfg.MissLimit)
                    {
                        if (t.State != TargetState.Down)
                        {
                            t.State = TargetState.Down;
                            t.DownSince = now;
                            _realertAtUnix[i] = now.ToUnixTimeSeconds() + _cfg.RealertSecs;
                            _bus.Publish(new MonEvent(now, EventSource.Icmp, t.Name, "down",
                                $"{t.Name} ({t.Address}) unreachable for {_misses[i]} consecutive probes", Severity.High));
                        }
                        else if (now.ToUnixTimeSeconds() >= _realertAtUnix[i])
                        {
                            _realertAtUnix[i] = now.ToUnixTimeSeconds() + _cfg.RealertSecs;
                            var dur = t.DownSince is { } ds ? (long)(now - ds).TotalSeconds : 0;
                            _bus.Publish(new MonEvent(now, EventSource.Icmp, t.Name, "still-down",
                                $"{t.Name} ({t.Address}) still down ({dur / 60.0:F0} min)", Severity.High));
                        }
                    }
                }
            }

            try { await Task.Delay(_cfg.ProbeIntervalSec * 1000, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { await _task; } catch { /* shutdown races are fine */ }
        _cts.Dispose();
    }
}
