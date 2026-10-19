namespace UdmMonitor.Core;

/// <summary>
/// Composes the phase-1 connectors: out-of-band ICMP probing, the ntfy stream from
/// the on-box watchdog, the heartbeat watch over that stream, and durable logging.
/// UI-free by design — a future Rust core (or a headless Windows service) can host
/// exactly these pieces behind the same EventBus surface.
/// </summary>
public sealed class MonitorEngine : IAsyncDisposable
{
    public MonitorConfig Config { get; }
    public EventBus Bus { get; } = new();
    public WatchdogState Watchdog { get; } = new();

    private JsonlEventLog? _log;
    private NtfySubscriber? _ntfy;
    private IcmpProber? _prober;
    private HeartbeatWatch? _hbWatch;

    public IReadOnlyList<TargetStatus> Targets => _prober?.Targets ?? Array.Empty<TargetStatus>();

    public MonitorEngine(MonitorConfig config)
    {
        Config = config;
    }

    public void Start()
    {
        _log = new JsonlEventLog(Bus, ResolveLogDir());
        _prober = new IcmpProber(Config, Bus);
        _ntfy = new NtfySubscriber(Config, Bus, Watchdog);
        _hbWatch = new HeartbeatWatch(Config, Bus, Watchdog);

        Bus.Publish(MonEvent.System("started",
            $"engine up; targets: {string.Join(", ", Config.Targets.Select(t => $"{t.Name}={t.Address}"))}; " +
            $"ntfy topic: {Config.NtfyTopic}"));
    }

    private string ResolveLogDir()
    {
        return Path.IsPathRooted(Config.LogPath)
            ? Config.LogPath
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, Config.LogPath));
    }

    public async ValueTask DisposeAsync()
    {
        Bus.Publish(MonEvent.System("stopped", "engine shutting down"));
        if (_ntfy is not null) await _ntfy.DisposeAsync();
        if (_hbWatch is not null) await _hbWatch.DisposeAsync();
        if (_prober is not null) await _prober.DisposeAsync();
        if (_log is not null) await _log.DisposeAsync();
    }
}
