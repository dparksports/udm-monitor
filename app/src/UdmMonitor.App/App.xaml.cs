using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using UdmMonitor.Core;

namespace UdmMonitor.App;

public partial class App : Application
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    private static Mutex? _singleton;
    private MonitorEngine? _engine;
    private TaskbarIcon? _tray;
    private MainWindow? _window;
    private ToastService? _toasts;
    private bool _reallyExit;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Contains("--smoke", StringComparer.OrdinalIgnoreCase))
        {
            // Task.Run keeps the smoke loop off the STA/dispatcher thread so its
            // async continuations can't deadlock against the blocked caller below.
            Task.Run(RunSmokeAsync).GetAwaiter().GetResult();
            Shutdown();
            return;
        }

        _singleton = new Mutex(true, @"Global\UdmMonitor.SingleInstance", out var isNew);
        if (!isNew)
        {
            MessageBox.Show("UdmMonitor is already running — check the tray area.", "UDM Monitor",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var config = MonitorConfig.LoadOrDefault(Path.Combine(AppContext.BaseDirectory, "udmmonitor.json"));
        _engine = new MonitorEngine(config);
        _engine.Start();

        _toasts = new ToastService(config);
        _window = new MainWindow(_engine);
        _window.Closing += (_, ce) =>
        {
            if (!_reallyExit)
            {
                ce.Cancel = true;
                _window.Hide(); // close-to-tray: monitoring never stops with the window
            }
        };

        _tray = BuildTray();
        StartUiPump();
        _window.Show();
    }

    private TaskbarIcon BuildTray()
    {
        var showItem = new MenuItem { Header = "Open dashboard" };
        showItem.Click += (_, _) => ShowWindow();
        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => ExitApp();

        var menu = new ContextMenu();
        menu.Items.Add(showItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);

        var icon = new TaskbarIcon
        {
            ToolTipText = "UDM Monitor",
            ContextMenu = menu,
            IconSource = new BitmapImage(new Uri("pack://application:,,,/Assets/app.ico")),
        };
        icon.TrayLeftMouseUp += (_, _) => ShowWindow();
        return icon;
    }

    private void ShowWindow()
    {
        if (_window is null) return;
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void ExitApp()
    {
        _reallyExit = true;
        _tray?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_engine is not null) _engine.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.OnExit(e);
    }

    /// <summary>One bus subscription feeds both the dashboard list and the toast path.</summary>
    private void StartUiPump()
    {
        var reader = _engine!.Bus.Subscribe();
        _ = Task.Run(async () =>
        {
            await foreach (var ev in reader.ReadAllAsync())
            {
                Dispatcher.Invoke(() =>
                {
                    _window?.AddEvent(ev);
                    if (ev.Severity == Severity.High)
                        _toasts?.Notify(ev.Subject, ev.Message);
                });
            }
        });
    }

    /// <summary>Headless verification path: `UdmMonitor --smoke` runs the engine for 20 s and prints every event.</summary>
    private async Task RunSmokeAsync()
    {
        AttachConsole(-1); // WinExe has no console of its own; borrow the calling terminal's
        var config = MonitorConfig.LoadOrDefault(Path.Combine(AppContext.BaseDirectory, "udmmonitor.json"));
        var engine = new MonitorEngine(config);
        var reader = engine.Bus.Subscribe();
        engine.Start();

        Console.WriteLine(
            $"[smoke] 20s run; targets: {string.Join(", ", config.Targets.Select(t => t.Address))}; ntfy topic: {config.NtfyTopic}");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await foreach (var ev in reader.ReadAllAsync(cts.Token))
                Console.WriteLine(
                    $"[smoke] {ev.Timestamp.LocalDateTime:HH:mm:ss} {ev.Source,-13} {ev.Kind,-11} {ev.Subject}: {ev.Message}");
        }
        catch (OperationCanceledException) { }

        await engine.DisposeAsync();
        Console.WriteLine("[smoke] done");
    }
}
