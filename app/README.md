# UDM Monitor

Native Windows tray app (C# / .NET 10 / WPF) that watches your UDM Pro network
out-of-band, complementing the on-box `cam-watchdog.sh`.

## What it does (phase 1)

- **ICMP probing from this PC** — the check the on-box watchdog can never run.
  If the UDM itself dies, this side still sees it. Debounce and re-alert
  semantics mirror `cam-watchdog.conf` (3 misses = down, re-alert every 10 min).
- **ntfy stream** — subscribes over WebSocket to the topic the on-box watchdog
  pushes to (`udm9-bbfe760697f194d8b211305c`), so camera-loss, evil-twin, IPS
  and WPA-drift alerts arrive sub-second as native Windows toasts.
- **Watchdog heartbeat watch** — the on-box script heartbeats daily; if it goes
  silent for 25 h, this PC alerts (only possible from out-of-band).
- **Durable history** — every event is appended to `logs/events.jsonl`
  (rotated at 5 MB), replacing ad-hoc `Add-Content` log loops.

## Run

    dotnet run --project src/UdmMonitor.App/UdmMonitor.App.csproj

Headless 20 s verification run:

    dotnet run --project src/UdmMonitor.App/UdmMonitor.App.csproj -- --smoke

The window closes to the tray; monitoring keeps running. High-severity events
raise Windows toasts. Single instance (mutex-guarded).

## Config

`udmmonitor.json` is created next to the exe on first run. Defaults mirror
`watchdog/cam-watchdog.conf`. Key values:

| Key | Default | Meaning |
|---|---|---|
| `ntfyTopic` | `udm9-bbfe...` | Topic the on-box watchdog pushes to |
| `targets` | gateway, camera, honeypot, internet | What gets pinged |
| `probeIntervalSec` / `missLimit` | 5 / 3 | Consecutive misses before "down" |
| `realertSecs` | 600 | Cooldown between still-down alerts |
| `heartbeatTimeoutSec` | 90000 | Silence before "watchdog dead" (25 h) |

Note: hosts that legitimately don't answer ICMP (e.g. the honeypot, or the
camera if this PC lacks a route to its VLAN) will show as "down" — remove them
from `targets` or rely on the on-box ARP check, which flows in via ntfy and is
the authoritative camera signal.

## Layout

- `src/UdmMonitor.Core` — UI-free engine (event bus, ICMP prober, ntfy
  subscriber, heartbeat watch, JSONL log). Kept dependency-free so a headless
  service host or a Rust core can reuse it verbatim later.
- `src/UdmMonitor.App` — WPF shell: tray, toasts, dashboard.

## Roadmap

- Phase 2: UniFi controller events WebSocket, SNMP metrics, live dashboard
  charts, credentials in Windows Credential Manager.
- Phase 3: syslog ingestion, SQLite history + correlation, headless service
  mode (Core hosted without UI).
