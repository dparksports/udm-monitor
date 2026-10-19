using Microsoft.Toolkit.Uwp.Notifications;
using UdmMonitor.Core;

namespace UdmMonitor.App;

/// <summary>Native Windows toasts for high-severity events; everything else stays in the dashboard.</summary>
public sealed class ToastService
{
    private readonly MonitorConfig _cfg;

    public ToastService(MonitorConfig cfg) => _cfg = cfg;

    public void Notify(string title, string body)
    {
        if (!_cfg.ToastsEnabled) return;
        try
        {
            new ToastContentBuilder()
                .AddText(title)
                .AddText(body)
                .Show();
        }
        catch
        {
            // toast infrastructure unavailable (e.g. stripped Windows) — never crash the monitor
        }
    }
}
