# Architecture

Status: draft. The Monitoring core (`Udjat.Core`) is built and unit-tested. The Windows sensor
source (`Udjat.Windows`, docs/SENSORS.md) is built too, with unit-tested mapping rules.
Settings, alerts, protection and the email and Telegram channels (docs/ALERTS.md) are built and
tested too. The WPF app (toast and sound channels, secret store, tray, dashboard, settings window,
autostart) is specified in docs/APP.md; its skeleton, secrets and autostart are built. The dashboard is being
redesigned and built (docs/UI.md: a panel per device, dark and light themes). The settings window and packaging are not built yet.
Phase 3 findings and the driver decision are in docs/RESEARCH.md.
Items marked **[verify]** must be confirmed before any code depends on them.

## Overview

```
 Sensors            Monitoring              Alerts / Actions
+-----------+     +----------------+     +-------------------+
| LHM       | --> | SensorMonitor  | --> | AlertDispatcher   |
| (behind   |     | - poll loop    |     |  - Toast          |
| ISensor-  |     | - thresholds   |     |  - Sound          |
| Source)   |     | - sustain time |     |  - Email          |
+-----------+     | - cooldown     |     |  - Telegram       |
                  +----------------+     | ProcessGuard      |
                         |               | AlertLog          |
                         v               +-------------------+
                  +----------------+
                  | UI (WPF)       |
                  | tray + dashboard + settings
                  +----------------+
```

One process, one exe, runs elevated (needed by the sensor driver).

## Stack

| Concern | Choice | License | Why |
|---|---|---|---|
| Runtime | .NET 10 (LTS, supported to 2028-11) | MIT | Verified |
| UI | WPF | MIT | Native Windows, mature, no extra dependency |
| Sensors | LibreHardwareMonitorLib 0.9.6 | MPL-2.0 (verified); embeds 12 LGPL-2.1 PawnIO modules (accepted) | Covers CPU + NVIDIA/AMD/Intel GPU, fans, drives, RAM |
| Charts | Small custom WPF controls (`RingGauge`, `Sparkline`) | built-in | A ring and a small line per panel do not justify ScottPlot, which brings SkiaSharp and about 10 MB of native code into an elevated process |
| Email | MailKit 4.18.2 | MIT (verified) | Microsoft recommends it over SmtpClient |
| Telegram | HttpClient -> Bot API | built-in | No dependency needed |
| Tray + Windows notification | WinForms NotifyIcon (balloon shows as toast on Win10/11) | built-in | No dependency needed |
| Sound | System.Media.SoundPlayer | built-in | No dependency needed |
| Secrets | Credential Manager via small P/Invoke wrapper | built-in | No dependency needed |
| Settings | System.Text.Json, %ProgramData%\Udjat\settings.json | built-in | Human-readable, no secrets inside; the folder only administrators can change (docs/REVIEW.md, S2) |
| Installer | Inno Setup 6 | Inno Setup license (free for commercial use, see RESEARCH.md) | Simple script; installs to Program Files only; the app, not the installer, creates the autostart task |
| Tests | xunit.v3 4.0.2, xunit.runner.visualstudio 4.0.1, Microsoft.NET.Test.Sdk 18.11.0 | Apache-2.0 / MIT (verified) | Unit tests for the monitoring logic. Test-only, never shipped |

Rule: prefer built-in .NET over a package. Every package needs a reason.

## Project layout

```
Udjat/
  Udjat.slnx
  global.json                  pins the .NET 10 SDK; selects the Microsoft.Testing.Platform test runner
  Directory.Build.props        settings shared by all projects (net10.0, nullable, warnings are errors)
  src/Udjat.Core/              class library (net10.0): no UI, no network, no Windows APIs
    Sensors/
      ISensorSource.cs         interface (swappable, e.g. Linux later)
      SensorKind.cs            CpuTemp, GpuTemp, GpuHotspot, GpuVram, Fan, Ram, DriveTemp, NvmeTemp,
                               CpuLoad, GpuLoad, GpuFan (the last three are display only)
      SensorReading.cs         record: id, kind, name, value
    Monitoring/
      AlertLevel.cs            Normal, Warning, Critical
      Threshold.cs             Warning + Critical limits for one sensor kind
      MonitorOptions.cs        poll interval, hysteresis, confirm readings, cooldown, sustain time
      MonitorEvents.cs         event payloads: SensorEvent, SensorStatus, PollResult
      ThresholdState.cs        per-sensor state machine (rules below)
      SensorMonitor.cs         poll loop, raises events
    Settings/                  AppSettings (model, defaults), SettingsStore (load, validate, safe save), SettingsForm,
                               SettingsModel and SettingsSaver (the settings page's logic), ISecretStore
    Alerts/                    ChannelKind, SensorKindInfo, AlertMessage, AlertText, ChannelRouter, IAlertChannel,
                               AlertDispatcher, MonitorHealth
    Protection/                ProcessNames, IProcessTable, ProcessGuard, AutoProtector
    Logging/                   AlertLog (alerts only, rotating), Redactor
    Status/                    ReadingHistory, StatusSummary, Notices: what the UI shows, as testable logic (docs/APP.md)
  src/Udjat.Windows/           class library (net10.0): the Windows platform layer. Renamed from
                               Udjat.Sensors.Windows in the app step (docs/APP.md)
    Sensors/                   RawSensor, CpuTempStatus, SensorMapper, LhmSensorSource (docs/SENSORS.md);
                               the only code that touches LibreHardwareMonitor
    Secrets/                   SecretStore: Credential Manager through P/Invoke
    Autostart/                 AutostartTask: the Task Scheduler task, defined as XML; only from Program Files
    Processes/                 SystemProcessTable: the running programs, and whose each one is
    Storage/                   DataFolder: %ProgramData%\Udjat, made so only administrators can change it
    SingleInstance.cs          a second start shows the first instance's dashboard
  src/Udjat.Channels/          class library (net10.0): the network channels (docs/ALERTS.md)
    EmailChannel.cs            MailKit, TLS only
    TelegramChannel.cs         HttpClient -> Bot API
  src/Udjat/                   WPF app (net10.0-windows, requireAdministrator), references the three libraries
    Program.cs  AppHost.cs  Pipeline.cs   start, rebuild after a settings change, stop
    ShellOpen.cs               opens links and files without administrator rights
    Tray/                      TrayIcon, TrayIcons (status colours)
    Channels/                  ToastChannel, SoundChannel
    Dashboard/                 MainWindow (side menu, notices, pages), MainHost, MainView, OverviewPage, panels, RingGauge, Sparkline (docs/UI.md)
    Fans/                      FansPage
    Themes/                    Dark.xaml, Light.xaml (the same colour keys, switched at run time), Shared.xaml (the eye, icons),
                               Controls.xaml (side menu items), Inputs.xaml (text boxes, switches, buttons, scroll bar)
    Settings/                  SettingsPage with four tabs, SettingsController (test, files; save goes to SettingsSaver in Core)
  tests/Udjat.Tests/           xUnit v3; references the three libraries; fakes for the sensor source, the clock,
                               channels, processes and HTTP, plus a fake mail server
  tools/SensorProbe/           diagnostic: raw LHM sensors plus what Udjat reads from them (run as admin)
  installer/Udjat.iss          Inno Setup script (docs/DECISIONS.md, 2026-10-30)
  tools/build-installer.ps1    publishes the app and builds the installer; tools/make-icon.ps1 makes the .ico
  licenses/                    full license texts shipped next to the program; THIRD-PARTY-NOTICES.md lists every component
  docs/
```

Target: no file over ~300 lines; most well under 150.

## Monitoring core

`Udjat.Core` holds the logic that decides when to alert. It has no UI, network
or Windows code and only raises events, so it is fully unit-tested.
This section is its spec.

### Types

- `SensorReading(string Id, SensorKind Kind, string Name, double Value)`.
  `Id` is stable and unique per physical sensor (e.g. the LHM identifier);
  two drives are two readings of the same kind. The unit follows the kind:
  °C for temperatures, % used for VRAM and RAM, RPM for fans.
- `ISensorSource.ReadAll()` returns the readings it could get this time.
  A sensor it cannot read (CPU temp without PawnIO) is simply absent.
- `Threshold(double Warning, double Critical)`, set per `SensorKind`.
  A kind with no threshold is shown but never alerts. Fans have none by default.
- `AlertLevel`: Normal < Warning < Critical.

### MonitorOptions (defaults)

| Option | Default | Meaning |
|---|---|---|
| PollInterval | 2 s | Time between two reads of all sensors |
| Hysteresis | 3 | In the sensor's unit (°C or % points). A level is left only when the value is this far below its limit |
| ConfirmReadings | 2 | Consecutive readings at or above a limit before the level rises (filters spikes) |
| Cooldown | 10 min | Minimum time between two alerts of the same level for the same sensor |
| SustainedCriticalAfter | 60 s | Critical held this long raises CriticalSustained (auto-protect) |

Bad settings fail at construction with `ArgumentException`: Warning >= Critical,
Hysteresis < 0, ConfirmReadings < 1, PollInterval <= 0, negative Cooldown or
SustainedCriticalAfter. `SensorMonitor` checks them all up front, so a bad
setting fails at startup, not in the middle of a poll.

### ThresholdState rules

One instance per sensor `Id`. `Update(value, now)` returns
`StateUpdate(Previous, Current, SendAlert, SendRecovered, CriticalSustained)`.
With W = Warning, C = Critical, h = Hysteresis, N = ConfirmReadings:

1. Count consecutive readings >= W and consecutive readings >= C. A lower
   reading resets that count. A value exactly at a limit counts as over it.
2. Rise: Critical once the C-count reaches N; otherwise Warning once the
   W-count reaches N.
3. Hold: Critical holds while value >= C - h; Warning holds while value >= W - h.
   Below that the level falls at once (no confirmation), possibly straight to Normal.
4. New level = the higher of steps 2 and 3.
5. `SendAlert` on every rise (Normal->Warning, Normal->Critical, Warning->Critical),
   unless an alert of that same level was sent for this sensor less than
   Cooldown ago. Cooldown is per level: a recent Warning alert never silences
   a Critical one.
6. An episode runs from leaving Normal until returning to Normal.
   `SendRecovered` when the level returns to Normal, only if an alert was sent
   during the episode. Critical -> Warning sends nothing.
7. `CriticalSustained` is true once per Critical stay: on the first update where
   now - (time the level became Critical) >= SustainedCriticalAfter.
   Dropping below Critical ends the stay; a later stay can fire again.

Example, W = 80, C = 90, h = 3, N = 2: readings 85, 95 -> Warning (one alert);
next 95 -> Critical (second alert); 88 -> still Critical; 86 -> Warning, silent;
76 -> Normal, "recovered".

### SensorMonitor

(`Monitor` would clash with `System.Threading.Monitor`.)

- Built with `(ISensorSource, IReadOnlyDictionary<SensorKind, Threshold>,
  MonitorOptions, TimeProvider?)`. Settings are fixed for its lifetime; when the
  user changes them, the app builds a new monitor.
- `PollOnce()`: read all sensors, update the state of every sensor whose kind has
  a threshold (state created on first sight), raise events. A sensor absent from
  a poll, or with a NaN/infinite value, keeps its state unchanged; such a value
  is also left out of `Polled`. If the source throws, raise `PollFailed` and
  change nothing. `PollOnce()` itself never throws.
- `RunAsync(CancellationToken)`: poll at once, then every PollInterval via
  `PeriodicTimer` on the TimeProvider. No exception ends the loop; only
  cancellation does, and that ends it normally (no exception). A token that is
  already cancelled polls nothing.
- Events are raised on the polling thread: `Polled` first, then the alert-type
  events in sensor order. Each subscriber is called separately; one that throws
  is reported through `PollFailed` and cannot stop the others or the loop.
  UI handlers must marshal to the UI thread; slow channels (email, Telegram)
  must queue their work and return quickly.

| Event | Payload | When |
|---|---|---|
| `Polled` | `PollResult(Time, IReadOnlyList<SensorStatus(Reading, Level)>)` | Every poll: dashboard buffer, tray colour |
| `AlertRaised` | `SensorEvent(Reading, Level, Time)` | `SendAlert` |
| `Recovered` | `SensorEvent` | `SendRecovered` (to the channels that heard the alert, see docs/ALERTS.md) |
| `CriticalSustained` | `SensorEvent` | `CriticalSustained`; ProcessGuard acts only if auto-protect is on |
| `PollFailed` | `Exception` | The source or an event handler threw |

Time comes from the built-in `TimeProvider`; tests use a small fake clock,
no extra package. The dashboard's rolling chart buffer (`Status/ReadingHistory`) is fed by `Polled`.

## Windows sensor source

Full spec in docs/SENSORS.md. In short: a thin `LhmSensorSource` opens LHM with only the
groups Udjat needs and flattens its tree into plain `RawSensor` records. A pure, unit-tested
`SensorMapper` then picks the few sensors that matter, builds unique ids and reports whether
CPU temperature is available (`CpuTempStatus`). A missing PawnIO driver shows up only as CPU
sensors with no values, and the app explains it to the user.

## Settings, alerts and protection

Full spec in docs/ALERTS.md: the settings file and default thresholds, which channel hears about
which message, the queue that keeps the poll loop off the network, the alerts-only log,
ProcessGuard's allow-list and deny-list rules, and the "monitoring stopped" check.

## The Windows app

Full spec in docs/APP.md. `AppHost` builds the pipeline from the settings, rebuilds it after a
change (the sensor source and the chart history survive), and stops it cleanly. The tray icon's
colour comes from `StatusSummary`; the dashboard shows notices, one tile per reading and two
30-minute charts; the settings window has four tabs and a Test button per channel.

## Security notes

- Runs elevated because the sensor driver requires it. Therefore:
  - minimal dependencies, all pinned;
  - no listening ports, no incoming network;
  - outgoing only to the user's SMTP host and api.telegram.org, TLS only.
- ProcessGuard only ever touches process names on the user's allow-list, and only processes of the same account and Windows session as Udjat;
  never by PID from outside input; never system processes.
- Secrets live in Credential Manager; never logged; exceptions are sanitized.
- Sensor driver: LHM 0.9.6 ships no kernel driver. It embeds PawnIO modules and
  uses the PawnIO driver only if the user installed it (RESEARCH.md). The old
  WinRing0 driver, flagged by Windows Defender, is not used. Document this in README.
- LHM is used read-only with only five sensor groups enabled; Udjat never calls
  its fan-control API.
- The manifest asks for administrator rights (`requireAdministrator`), so Udjat never runs
  half-blind. Because it is elevated, it opens links and files through `explorer.exe`, so a
  browser or editor never starts with administrator rights.
- Future option (not v1): split into an elevated sensor service + normal UI.

## Autostart

A Task Scheduler task named `Udjat`: at the user's logon, highest privileges (no UAC prompt),
started with `--tray`. It is defined as XML because the `schtasks` defaults would stop Udjat
after 72 hours and skip it on battery. The app creates, updates or removes the task to match the
setting at every start, so the installer only has to delete it on uninstall. Details in docs/APP.md.

## Open questions

- Default thresholds: initial values and reasons are in docs/ALERTS.md, several
  marked **[verify]**. Revisit them with probe data from testers (AMD and Intel
  GPUs, AMD CPUs, other drives).
- LHM on real hardware: the probe has run once, on the owner's PC (Intel CPU,
  NVIDIA GPU, no PawnIO). Results and mapping traps are in RESEARCH.md. Still
  untested: CPU package temp with PawnIO installed, AMD CPUs, GPU hotspot on
  AMD and Intel GPUs. Ask testers to run the probe.
- `Mono.Posix.NETStandard` is Linux-only code but copies two native helpers
  (`MonoPosixHelper.dll`, `libMonoPosixHelper.dll`, about 1.6 MB) next to the exe
  on Windows too. Check whether they can be left out of the publish.
- WPF self-contained publish size.
- Inno Setup license wording (see RESEARCH.md).
