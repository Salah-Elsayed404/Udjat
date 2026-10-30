# Settings, alerts and protection

Spec for everything between the Monitoring core's events and the user: the settings and their
defaults, which channel hears about what, the alert log, auto-protect and the "monitoring stopped"
check. The WPF window, tray and installer are specified later.

**Status: built and tested**, except the parts that need the WPF app (toast, sound, `SecretStore`,
the CPU notice, autostart). Differences between this spec and the code are listed at the end.

## Where the code goes

| Project | New folders / files | Why there |
|---|---|---|
| `Udjat.Core` | `Settings/` AppSettings, SettingsStore; `Alerts/` SensorKindInfo, AlertMessage, AlertText, IAlertChannel, AlertDispatcher, MonitorHealth; `Protection/` IProcessTable, ProcessGuard (the real process table is `SystemProcessTable` in `Udjat.Windows`); `Logging/` AlertLog, Redactor | Pure logic or plain .NET (files, `System.Diagnostics.Process`): no UI, no network, no Windows API. All of it unit-testable |
| `Udjat.Channels` (new, `net10.0`) | EmailChannel (MailKit), TelegramChannel (HttpClient) | The network code, kept out of the core but still testable (fake HTTP handler; message building) |
| `Udjat` (WPF app, later) | ToastChannel, SoundChannel, SecretStore (Credential Manager), autostart, UI | Needs WPF/WinForms or Windows APIs |

## Settings

One JSON file, `%ProgramData%\Udjat\settings.json`. No secrets in it, ever. Enums are written as
strings. A missing field takes its default, and unknown fields are ignored. The folder can be changed only by administrators
(everyone can read it), so editing the file by hand needs an administrator; the Settings page is the normal way.

```json
{
  "schemaVersion": 1,
  "thresholds": {
    "CpuTemp":    { "enabled": true,  "warning": 90,  "critical": 97 },
    "GpuTemp":    { "enabled": true,  "warning": 84,  "critical": 90 },
    "GpuHotspot": { "enabled": true,  "warning": 100, "critical": 108 },
    "GpuVram":    { "enabled": false, "warning": 95,  "critical": 99 },
    "Ram":        { "enabled": true,  "warning": 97,  "critical": 99 },
    "DriveTemp":  { "enabled": true,  "warning": 50,  "critical": 55 },
    "NvmeTemp":   { "enabled": true,  "warning": 75,  "critical": 82 }
  },
  "routing":   { "warning": ["Toast"], "critical": ["Toast", "Sound", "Email", "Telegram"] },
  "toast":     { "enabled": true },
  "sound":     { "enabled": true },
  "email":     { "enabled": false, "host": "", "port": 587, "security": "StartTls", "username": "", "from": "", "to": "" },
  "telegram":  { "enabled": false, "chatId": "" },
  "cooldownMinutes": 10,
  "autoProtect": { "enabled": false, "afterSeconds": 60, "processNames": [] },
  "autostart": true,
  "temperatureUnit": "Celsius",
  "advanced":  { "pollSeconds": 2, "hysteresis": 3, "confirmReadings": 2 }
}
```

- A channel fires only if it is enabled **and** listed for that level. With the defaults, Warning
  shows a Windows notification and Critical uses every channel the user has set up.
- Fans have no thresholds entry at all: they are shown and never alerted (DECISIONS.md).
- `temperatureUnit` is `Celsius` or `Fahrenheit`. It only changes what is shown (the dashboard, charts, alert texts and log, the tray
  tooltip, the limits typed on the Alerts tab); the limits and readings in the file and in the core are always Celsius.
- `AppSettings` turns itself into what the core needs: `ToThresholds()` (enabled kinds only) and
  `ToMonitorOptions()` (`cooldownMinutes`, `autoProtect.afterSeconds`, `advanced`).
- The UI shows thresholds, channels, routing, cooldown, auto-protect and autostart. `advanced` is
  file-only.

### Validation (`SettingsStore`)

- Thresholds: `Threshold.Validate()` (Warning < Critical, finite). The UI keeps percentages within 0 to 100.
- Email, when enabled: host not empty; port 1 to 65535; `security` is `StartTls` or `SslOnConnect`
  (never plain text); `from` and `to` are valid addresses.
- Telegram, when enabled: chat id is an integer (group chats are negative).
- Auto-protect names: see ProcessGuard below.
- Load: if the file is missing, use the defaults and save them. If it cannot be read or fails
  validation, rename it to `settings.invalid.json`, start with the defaults, and tell the user once.
- Save: write a temporary file next to it, then move it over the old one, so a crash never leaves
  half a file.
- Settings are fixed while monitoring runs. A change builds a new monitor and dispatcher.

### Default thresholds and why

These are starting points, chosen so a normal heavy AI load stays quiet. Revisit them with probe
data from testers.

| Kind | Warning / Critical | Reason |
|---|---|---|
| CpuTemp | 90 / 97 | Intel 13th gen chips are limited at 100 °C; some AMD chips run at 90 to 95 °C by design under full load **[verify]** |
| GpuTemp | 84 / 90 | Many NVIDIA cards start lowering clocks around 83 °C **[verify]**. The owner's card ran at 72 to 74 °C under AI load |
| GpuHotspot | 100 / 108 | The hotspot normally runs 10 to 20 °C above the core (owner: 84 to 88 °C at a core of 72 to 74 °C). AMD has said up to about 110 °C is acceptable **[verify]** |
| GpuVram | off (95 / 99 if turned on) | Full VRAM is normal for local AI: model runners fill it on purpose. Alerting by default would nag |
| Ram | 97 / 99 | The owner's normal AI load sits at 92 to 95 %. When RAM is really full, Windows swaps and can freeze |
| DriveTemp | 50 / 55 | Hard disks are commonly rated for up to 60 °C. The owner's HDD sat at 33 to 34 °C |
| NvmeTemp | 75 / 82 | Many NVMe SSDs slow down somewhere around 75 to 85 °C **[verify]**. The owner's SSD sat at 44 to 45 °C and reports its own limits as 89 / 94 |

Later (not v1): use a drive's own reported limits, or the CPU's own limit, as per-device defaults.
That needs thresholds per sensor instead of per kind.

## Secrets

- SMTP password and Telegram bot token live only in Windows Credential Manager, as generic credentials
  `Udjat/SmtpPassword` and `Udjat/TelegramBotToken` (per user, this machine). `SecretStore` in `Udjat.Windows`
  (behind the `ISecretStore` interface) reads and writes them through a small P/Invoke wrapper (CredRead / CredWrite / CredDelete).
- The channels receive the secret as a string when they are built. They never store, log or show it.
- The Telegram token is checked against `^\d+:[A-Za-z0-9_-]+$` before use, because it goes into a URL path.
- `Redactor.Redact(text, secrets)` replaces every known secret with `***`. Every error text passes
  through it before it reaches the log or the screen.

## Messages and routing

`AlertDispatcher` subscribes to the `SensorMonitor` events and to `MonitorHealth`, turns each one
into an `AlertMessage`, and decides which channels get it.

| Message | Comes from | Channels |
|---|---|---|
| Alert, Warning | `AlertRaised` (Warning) | `routing.warning` |
| Alert, Critical | `AlertRaised` (Critical) | `routing.critical` |
| Recovered | `Recovered` | The channels that received this episode's highest alert, except Sound |
| Auto-protect acted | ProcessGuard result after `CriticalSustained` | `routing.critical`, except Sound |
| Monitoring stopped | MonitorHealth: no successful poll for 60 s | `routing.critical`, except Sound |
| Monitoring resumed | MonitorHealth: polls work again | The same channels as "stopped" |

- Sound is an alarm, so it only plays for Warning and Critical alerts.
- The dispatcher remembers, per sensor id, the highest alert level actually sent in the current
  episode (a level silenced by the cooldown does not count), so "recovered" reaches the same people
  as the alert. This replaces the earlier "recovered = toast only".
- Only enabled channels are used. Toast and Sound are local; Email and Telegram reach users away from the PC.

### Delivery

- Event handlers only build the message and put it in a queue (`System.Threading.Channels`), so the
  poll loop never waits on the network.
- One background consumer sends each message to its channels one after the other, with a 30 s timeout
  per channel. No retries in v1; a failure is logged.
- `IAlertChannel { ChannelKind Kind; Task SendAsync(AlertMessage message, CancellationToken ct); }`.
  A channel reports a failure by throwing. The dispatcher catches it, redacts it, and records a short
  reason (exception type plus message), never a URL or a secret.
- `SendTestAsync(ChannelKind)` sends a test message to one channel, for the "Test" button in settings.

### Wording (`AlertText`)

All user-visible text is built in one class, in English for v1, so translation later is one file.
`SensorKindInfo` gives each kind a label and unit: CpuTemp "CPU temperature" °C, GpuTemp "GPU
temperature" °C, GpuHotspot "GPU hotspot" °C, GpuVram "GPU memory" %, Fan "Fan" RPM, Ram "RAM" %,
DriveTemp "Drive temperature" °C, NvmeTemp "NVMe SSD temperature" °C. Values are rounded to whole
numbers.

Examples:
- Title "Udjat: GPU hotspot is CRITICAL (108 °C)". Body "NVIDIA GeForce RTX 3060: GPU hotspot is
  108 °C. Critical limit: 108 °C. 18:03, 29 Oct 2026."
- "Udjat: GPU hotspot is back to normal (85 °C)".
- "Udjat auto-protect closed ollama.exe (2 processes) because the GPU hotspot of NVIDIA GeForce
  RTX 3060 stayed critical for 60 s."
- "Udjat cannot read the sensors (since 18:03)." Then "Udjat is reading the sensors again."

## Alert log

- `%ProgramData%\Udjat\alerts.log`: plain text, one line per message, readable by anyone.
  For example: `2026-10-29 18:03:12 | CRITICAL | GPU hotspot | NVIDIA GeForce RTX 3060 | 108 °C | toast: sent, email: failed (TimeoutException), telegram: sent`
- Only alerts, recoveries, auto-protect actions and monitoring stopped/resumed. No sensor history.
- When the file passes 1 MB, it becomes `alerts.old.log` (replacing any older one) and a new file starts.
- Writes are serialized with a lock. A failing log write never stops an alert.

## Auto-protect (ProcessGuard)

Off by default. When a sensor stays Critical for `afterSeconds` (the core's `CriticalSustained`,
once per Critical stay) and auto-protect is on, ProcessGuard closes the allow-listed processes.

- Allow-list entries are plain process names. Trim them, ignore case and a trailing ".exe", and reject
  anything with a path separator, a wildcard, or nothing left.
- A built-in deny list wins over the allow-list: System, Idle, smss, csrss, wininit, winlogon, services,
  lsass, svchost, explorer, dwm, fontdrvhost, and Udjat itself (its own process id).
- Only processes whose name matches the list are closed, one by one with `Kill()`. Their child processes
  are not touched unless they are on the list too.
- **Only the user's own programs.** Udjat runs as administrator, and the list comes from a settings file, so a process is closed
  only if it runs in the same Windows session and under the same account as Udjat (`IProcessTable.Me` and `OwnerOf`). Services,
  other people's programs and any process whose owner Windows will not tell are left alone and reported as such. Ollama and
  LM Studio, started by the user, qualify (docs/REVIEW.md, S3).
- `IProcessTable { ProcessOwner? Me; List(); ProcessOwner? OwnerOf(process); Kill(process); }` keeps the rules
  testable; `SystemProcessTable` (in `Udjat.Windows`) is the real one.
- The result lists each process as closed, failed (with a reason), protected, or left alone because it belongs to another account or session. It goes to the dispatcher
  ("Auto-protect acted") and to the log. If nothing matched, it says so.

## Monitoring stopped (MonitorHealth)

- `OnPolled(time)` on every `Polled`. A timer checks every 15 s.
- No successful poll for 60 s gives one "stopped" message. The next successful poll gives one
  "resumed" message. A source that hangs inside `ReadAll` is caught too, because the timer does not
  depend on the poll loop.
- `PollFailed` is not used for this, because it also fires when an event handler throws.
- Not in v1: noticing that a single sensor disappeared while the others still read.

## CPU temperature notice (app)

At startup, and after each settings change, the app checks `LhmSensorSource.CpuTemp` after the first poll:
- `NoValues`, not elevated: "Run Udjat as administrator to read all sensors."
- `NoValues`, elevated: "CPU temperature needs the free PawnIO driver (pawnio.eu). Install it, then
  restart Udjat." Shown once as a notification and permanently as a line in the dashboard, with a
  "don't show again" option.
- `NotFound`: a quiet line in the dashboard only.
This is local only, never email or Telegram.

## Differences between this spec and the code

Things that were settled while building, so the spec above does not mislead:

- Extra pieces: `ChannelKind` and `ChannelRouter` (the routing rules as a pure function, tested alone),
  `ProcessNames` (the allow-list name rule, shared by settings validation and ProcessGuard),
  `AutoProtector` (the small piece that connects `CriticalSustained` to ProcessGuard and the dispatcher),
  `DeliveryResult`, and `AlertDispatcher.Complete()` (finish the queue, used at shutdown and in tests).
- `AlertDispatcher` takes the list of secrets to hide, and `MonitorHealth` raises `Stopped` (with the time of
  the last good poll) and `Resumed` (with the time of the poll that worked).
- "Recovered" goes to the channels the episode's highest alert was *routed* to, whether or not each delivery worked.
- A failed delivery is logged as `channel: failed (ExceptionType: message)`, on one line, cut at 120 characters, secrets hidden.
- Settings are read by laying the user's JSON over the defaults, so a missing field takes its default at every
  level (also inside one threshold). Comments and trailing commas in the file are allowed.
- `SystemProcessTable.Kill` re-checks the process name and owner just before closing it, in case Windows has given the id to another program.
- `EmailChannel` has a second constructor that takes the sending function, for tests and other transports.
  A test with a fake mail server proves that nothing (no password, no email) is sent if the server cannot encrypt.
- Alert wording not in the examples above: a Warning has the title "Udjat: {what} warning ({value})".

## Build order (each step builds and passes its tests before the next)

1. `SensorKindInfo`, `AppSettings` with defaults and conversions, `SettingsStore` with validation,
   invalid-file handling and safe save. Tests use a temporary folder.
2. `Redactor` and `AlertLog`, including rotation.
3. `AlertMessage`, `AlertText`, and the routing rules (which channels for which message).
4. `AlertDispatcher` (queue, timeout, outcomes, episode memory, test send) and `MonitorHealth`, with
   fake channels and the fake clock.
5. `ProcessGuard` with a fake process table.
6. `Udjat.Channels`: TelegramChannel (fake HTTP handler: URL, body, token check, redacted errors), then
   EmailChannel (message building, TLS only). Check MailKit's dependency licenses first and record them.
