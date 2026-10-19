using System.Text.Json;
using System.Text.Json.Serialization;

namespace UdmMonitor.Core;

public sealed class ProbeTarget
{
    [JsonPropertyName("address")] public string Address { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
}

/// <summary>
/// Defaults mirror the on-box cam-watchdog.conf so both sides agree on debounce and
/// re-alert semantics (3 misses to declare down, 10 min re-alert, daily heartbeat).
/// </summary>
public sealed class MonitorConfig
{
    [JsonPropertyName("ntfyTopic")] public string NtfyTopic { get; set; } = "udm9-bbfe760697f194d8b211305c";

    [JsonPropertyName("probeIntervalSec")] public int ProbeIntervalSec { get; set; } = 5;
    [JsonPropertyName("missLimit")] public int MissLimit { get; set; } = 3;
    [JsonPropertyName("realertSecs")] public int RealertSecs { get; set; } = 600;
    [JsonPropertyName("probeTimeoutMs")] public int ProbeTimeoutMs { get; set; } = 1500;

    /// <summary>On-box watchdog heartbeats daily (86400s); alert after 25h of silence.</summary>
    [JsonPropertyName("heartbeatTimeoutSec")] public int HeartbeatTimeoutSec { get; set; } = 90000;

    [JsonPropertyName("toastsEnabled")] public bool ToastsEnabled { get; set; } = true;

    /// <summary>Relative paths resolve against the exe directory.</summary>
    [JsonPropertyName("logPath")] public string LogPath { get; set; } = "logs";

    [JsonPropertyName("targets")] public List<ProbeTarget> Targets { get; set; } = new()
    {
        new ProbeTarget { Address = "192.168.2.1", Name = "UDM Pro (gateway)" },
        new ProbeTarget { Address = "192.168.3.146", Name = "Camera" },
        new ProbeTarget { Address = "192.168.2.2", Name = "Honeypot" },
        new ProbeTarget { Address = "1.1.1.1", Name = "Internet" },
    };

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static MonitorConfig LoadOrDefault(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var cfg = JsonSerializer.Deserialize<MonitorConfig>(File.ReadAllText(path), JsonOpts);
                if (cfg is not null) return cfg;
            }
        }
        catch
        {
            // corrupt config: fall through to defaults rather than refusing to monitor
        }

        var def = new MonitorConfig();
        try { File.WriteAllText(path, JsonSerializer.Serialize(def, JsonOpts)); } catch { }
        return def;
    }
}
