using System.Text.Json.Serialization;

namespace UdmMonitor.Core;

public enum Severity
{
    Info,
    Default,
    High,
}

public enum EventSource
{
    Icmp,
    Ntfy,
    HeartbeatWatch,
    System,
}

/// <summary>One observation flowing through the engine: a probe transition, an on-box alert, an engine lifecycle note.</summary>
public sealed record MonEvent(
    [property: JsonPropertyName("ts")] DateTimeOffset Timestamp,
    [property: JsonPropertyName("src")] EventSource Source,
    [property: JsonPropertyName("subject")] string Subject,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("msg")] string Message,
    [property: JsonPropertyName("sev")] Severity Severity)
{
    public static MonEvent System(string kind, string msg, Severity sev = Severity.Default)
        => new(DateTimeOffset.UtcNow, EventSource.System, "engine", kind, msg, sev);
}
