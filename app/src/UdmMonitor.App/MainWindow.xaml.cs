using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using UdmMonitor.Core;

namespace UdmMonitor.App;

public sealed record EventVm(string Time, string Source, string Subject, string Message);

public partial class MainWindow : Window
{
    private const int MaxRows = 500;

    private readonly MonitorEngine _engine;
    private readonly ObservableCollection<EventVm> _rows = new();
    private readonly DispatcherTimer _refresh;

    public MainWindow(MonitorEngine engine)
    {
        InitializeComponent();
        _engine = engine;
        Events.ItemsSource = _rows;
        UpdateStatus();

        _refresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refresh.Tick += (_, _) => UpdateStatus();
        _refresh.Start();
    }

    /// <summary>Called from the UI pump for every bus event; newest first.</summary>
    public void AddEvent(MonEvent e)
    {
        _rows.Insert(0, new EventVm(
            e.Timestamp.LocalDateTime.ToString("HH:mm:ss"),
            $"{e.Source}/{e.Kind}",
            e.Subject,
            e.Message));
        while (_rows.Count > MaxRows) _rows.RemoveAt(_rows.Count);
    }

    private void UpdateStatus()
    {
        var targets = _engine.Targets.Select(t =>
        {
            var state = t.State switch
            {
                TargetState.Up => $"UP {t.LastRttMs}ms",
                TargetState.Down => $"DOWN",
                _ => "…",
            };
            return $"{t.Name}: {state}";
        });

        var ntfy = _engine.Watchdog.IsConnected ? "ntfy: connected" : "ntfy: connecting…";
        var hb = _engine.Watchdog.LastHeartbeat is { } lb
            ? $"watchdog hb: {lb.LocalDateTime:ddd HH:mm}"
            : "watchdog hb: pending";

        StatusText.Text = $"{string.Join("   |   ", targets)}   |   {ntfy}   |   {hb}";
    }
}
