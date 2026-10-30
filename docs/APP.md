# The Windows app

Spec for `src/Udjat` (the WPF app) and the Windows-only pieces it needs. It wires the built
libraries together and adds the tray icon, the dashboard, the settings window, the local channels
(toast, sound), secrets and autostart. Installer and publishing come in the packaging step.
Status: built (tray, toast, sound, single instance, secrets, autostart, the window and the settings page), and fixed after the
security review of 2026-10-30 (docs/REVIEW.md). The Dashboard section below is replaced by docs/UI.md, and the settings window
is now a page of the main window (docs/UI.md, "Pages").

## Projects after this step

| Project | Change |
|---|---|
| `Udjat.Core` | New `Status/` folder (pure logic the UI shows, so it is testable): `ReadingHistory`, `StatusSummary`, `Notices`. Small additions: `LoadResult.IsNew`, `AppSettings.DismissedNotices`, a sleep guard inside `MonitorHealth.Check()`. Later: the settings page's logic (`SettingsModel`, `SettingsSaver`, `ISecretStore`) |
| `Udjat.Sensors.Windows` | **Renamed `Udjat.Windows`**: the Windows platform layer. Folders `Sensors/`, `Secrets/` (SecretStore), `Autostart/` (AutostartTask), `Processes/` (SystemProcessTable), `Storage/` (DataFolder), and `SingleInstance`. The tests reach it |
| `Udjat.Channels` | No change |
| `Udjat` (new) | `WinExe`, `net10.0-windows`, `UseWPF` + `UseWindowsForms` (for the tray icon only). References the three libraries. **No new packages** |

```
src/Udjat/
  Udjat.csproj  app.manifest
  Program.cs               entry: single instance, start arguments, the data folder check
  AppHost.cs               builds, rebuilds and stops the pipeline (the composition root)
  Pipeline.cs              the monitor, the dispatcher, the health check and auto-protect, wired together
  ShellOpen.cs             opens links and files without administrator rights
  Tray/TrayIcon.cs         NotifyIcon, menu, tooltip, status colour
  Tray/TrayIcons.cs        draws the four status icons at start
  Channels/                ToastChannel, SoundChannel, ChannelFactory
  Dashboard/               MainWindow (side menu), MainHost, MainView, OverviewPage, RingGauge, Sparkline (docs/UI.md)
  Fans/                    FansPage
  Settings/                SettingsPage, four tabs, SettingsController, SettingsBindings
  Themes/                  Dark, Light, Shared, Controls, Inputs, ThemeManager
```

## Administrator rights and autostart

- `app.manifest` asks for `requireAdministrator` and per-monitor DPI awareness. Udjat therefore always
  runs elevated, so there is no "half-working without admin" mode to explain. A manual start shows one
  UAC prompt; the normal start, at logon, shows none (below).
- **Opening links and files**: an elevated app must not start a browser or editor elevated. `ShellOpen`
  hands the path or URL to `explorer.exe`, which opens it as the normal user. Used for pawnio.eu, the
  alert log, the settings folder and the license files.
- **Autostart** (`Udjat.Windows/Autostart/AutostartTask`): a Task Scheduler task named `Udjat`, registered through the
  COM Task Scheduler (`ComTaskScheduler`) from an XML string, so there is no temporary file to swap. XML rather than plain
  `schtasks` switches, because the defaults are wrong for a watchdog:
  - logon trigger for the current user only (`WindowsIdentity.GetCurrent().Name`), `InteractiveToken`;
  - `RunLevel` `HighestAvailable` (no UAC prompt at logon);
  - `ExecutionTimeLimit` `PT0S`: the default of 72 hours would kill Udjat after three days;
  - `DisallowStartIfOnBatteries` and `StopIfGoingOnBatteries` false (laptops);
  - `MultipleInstancesPolicy` `IgnoreNew`; the action is `"<full path>\Udjat.exe" --tray`.
- `AutostartTask.BuildXml(exePath, user)` is pure and unit-tested; `Reconcile` works against the `ITaskScheduler` interface,
  so its rules are tested with a fake. **Tests never register a real task** (one opt-in test does, with a harmless task).
- At every start the app reconciles: setting on and task missing or pointing at another exe gives
  re-create; setting off and task present gives delete. A moved or reinstalled Udjat fixes itself.
- **Only from Program Files.** The task starts the exe with full rights and no prompt, so it may only point into a folder that
  standard users cannot change (`Environment.SpecialFolder.ProgramFiles`). A Udjat that runs from anywhere else (a build folder,
  Downloads, a folder directly under `C:\`) makes no task, deletes one that points outside Program Files, leaves one that
  points inside alone, and the dashboard says so ("Start with Windows only works when Udjat is installed in Program Files").
  The installer always installs there. Without this, anyone who could replace the exe would get administrator rights at the
  next logon (docs/REVIEW.md, S1).
- The task is made for the signed-in account. A standard user who elevates with an administrator's password runs Udjat as that
  administrator, so the task is not useful for them (docs/REVIEW.md, S6).
- **Data folder.** Settings and the alert log live in `%ProgramData%\Udjat`, made by `DataFolder.Prepare` at every start: created
  with its own permissions (SYSTEM and administrators full control, everyone else read only). A folder that other accounts could
  change (made by a normal user first, a junction, loose permissions) is renamed to `Udjat.unsafe-<id>` and replaced; if it still
  cannot be made safe, Udjat says so in a message box and does not start. Udjat writes there as administrator, so a folder a normal
  program can redirect would be a way to overwrite system files (docs/REVIEW.md, S2). `--data-dir` skips the check (testing only).
  Settings are per PC, the secrets stay per user in Credential Manager.

## Start, rebuild, stop (AppHost)

Start:
1. `SingleInstance`: a mutex `Local\Udjat.SingleInstance` and the events `Local\Udjat.Show` and `Local\Udjat.Exit`. If Udjat
   already runs, set the event (the first instance shows its dashboard, or with `--exit` closes) and exit. A real Udjat always
   creates both events and then the mutex, so one without the other means another program made them first: Udjat says so in a
   message box (never for `--exit`) instead of doing nothing (docs/REVIEW.md, S4). Then `DataFolder.Prepare` (above).
2. `SettingsStore.Load()`. A `Notice` becomes a dashboard notice and one toast. `IsNew` (first run)
   gives a welcome notice.
3. Read the secrets. Build the channels: Toast and Sound always; Email if it is enabled and the password
   exists; Telegram if it is enabled and the token exists. An enabled channel without its secret is not
   built (the log then says "channel not available") and gets a notice.
4. Open `LhmSensorSource` **once** and keep it for the life of the app (opening it is slow). If it fails,
   there is no monitoring: grey tray icon and a notice with the reason.
5. Build the pipeline: `SensorMonitor`, `AlertDispatcher`, `MonitorHealth(60 s, 15 s)`, and
   `AutoProtector(ProcessGuard(SystemProcessTable))` (it closes only the user's own programs, docs/ALERTS.md). Attach everything, run the monitor and the
   dispatcher on background tasks with one `CancellationTokenSource`.
6. Tray icon. Show the dashboard unless started with `--tray`.

Rebuild (after Save in settings): cancel the monitor, `Complete()` the dispatcher and wait (at most 5 s)
for its queue, dispose the health timer, then do steps 3 and 5 again with the new settings. The sensor
source, the tray and the chart history stay.

Stop (tray menu Exit, Windows logoff/shutdown via `SessionEnding`): the same as rebuild, then dispose the
sensor source and the tray icon.

**Sleep**: `MonitorHealth.Check()` notices when its own timer did not run for as long as "stopped" takes (the whole PC was
asleep) and gives the monitor a fresh start, so a wake-up does not send "Udjat cannot read the sensors" to Telegram. No
power-event handler is needed.

Errors: `DispatcherUnhandledException` is shown in a balloon and marked handled; a broken window must not
end the watchdog. There is no error log; the alert log stays alerts-only.

Threading: monitor and health events arrive on background threads. The app copies what it needs and uses
`Dispatcher.BeginInvoke` for anything on screen. It never blocks the poll thread.

## Tray icon

- WinForms `NotifyIcon`. Icons drawn once at start by `TrayIcons` (GDI+, a simple eye shape in a circle;
  sizes for the current DPI): green Normal, amber Warning, red Critical, grey for "not monitoring"
  (stopped or no sensor source). Handles from `GetHicon` are freed (`DestroyIcon`) after cloning.
- `StatusSummary` (Core) decides the state from the latest `PollResult`, whether health reported stopped,
  and whether a source exists. It also builds the tooltip (at most 127 characters), for example
  "Udjat: all normal", "Udjat: GPU hotspot CRITICAL (108 °C)", "Udjat: 2 warnings", "Udjat: not reading sensors".
  The state is never shown by colour alone: the tooltip and dashboard always say it in words.
- Menu: Open dashboard, Settings…, Open alert log, Exit. Double-click opens the dashboard.
- Closing the dashboard hides it. The first time, a toast says "Udjat keeps watching from the tray".
  Only Exit stops Udjat.

## Dashboard (one window)

**Replaced by docs/UI.md** (a panel per device, dark and light themes, neon eye). Only the notices below still apply.

From top to bottom:
1. **Status line**: the `StatusSummary` text, with its colour.
2. **Notices** (`Notices` in Core builds the list; each has an id, text, optional link or button, and
   whether it can be dismissed forever, stored in `AppSettings.DismissedNotices`):
   - CPU temperature `NoValues`: "CPU temperature needs the free PawnIO driver. Install it from pawnio.eu,
     then restart Udjat." Link opens via `ShellOpen`. Dismissible.
   - CPU temperature `NotFound`: "CPU temperature is not supported on this CPU." Dismissible.
   - Settings file was unusable (the `LoadResult` notice). Dismissible for this run only.
   - First run: "Udjat is watching. Open Settings to get alerts by email or Telegram." Dismissible.
   - An enabled channel without its secret: "Email is on but has no password. Open Settings." Not dismissible.
   - Start with Windows is on but Udjat does not run from Program Files: "Start with Windows only works when Udjat is
     installed in Program Files. Use the installer to turn it on." Dismissible.
   - The sensor source failed to open: the reason. Not dismissible.
   (No "run as administrator" notice: the manifest makes that impossible.)
`ReadingHistory` (Core): a fixed-size ring buffer per reading id, `(time, value)`, filled from every
`Polled`. Size = 30 minutes / poll interval. Thread-safe; each `Sparkline` takes a copy. **No chart package**: ScottPlot would
bring SkiaSharp and about 10 MB of native code into an elevated process for a few small charts.

## Settings window

**Now a page of the main window**, not a modal window (docs/UI.md, "Pages"). Four tabs. Save validates everything
(`AppSettings.Validate()`); an error is shown in the save bar in words, and nothing is saved. Discard goes back to what was saved.

1. **Alerts**: one row per kind (not fans): on/off, Warning, Critical (with the unit). "Reset to defaults".
   Cooldown in minutes.
2. **Channels**:
   - Windows notification on/off; sound on/off.
   - Email: on/off, server, port, security (STARTTLS or SSL), username, password, from, to.
   - Telegram: on/off, bot token, chat id, and a short how-to ("create a bot with @BotFather, send it a
     message, then use the chat id").
   - Routing: two rows of check boxes, "Warning goes to" and "Critical goes to".
   - A **Test** button per channel. It builds a throwaway `AlertDispatcher` from the values *on screen*
     (unsaved), calls `SendTestAsync`, and shows the result. Nothing is saved or logged.
3. **Auto-protect**: on/off, seconds at Critical, and the program names (one per line). A plain warning:
   "Only these programs are ever closed. System programs and Udjat itself are never closed."
4. **General**: start with Windows (autostart), CPU temperature status with the PawnIO link, open the
   alert log, open the settings folder, version, license and third-party notices.

Secrets: the password and token boxes are `PasswordBox`es and stay empty. "Saved" shows next to them if a
secret exists. Typing a new value replaces it on Save; a "Remove" link deletes it. Values are never shown back.

On Save (`SettingsSaver` in Core, so it is unit-tested with fakes): check every value and the length of the secrets first, then
apply autostart, write or delete the secrets, keep and write the settings (`SettingsStore.Save`), then `AppHost.Rebuild`.
A problem is shown in the save bar and nothing after it is done. If autostart cannot be set up here (not in Program Files), the
rest is saved and the bar says so. A Test button always comes back, even after an unexpected error.

## Secrets (Udjat.Windows/Secrets/SecretStore)

- P/Invoke to `advapi32`: `CredWriteW`, `CredReadW`, `CredDeleteW`, `CredFree`. Generic credentials,
  `CRED_PERSIST_LOCAL_MACHINE`, targets `Udjat/SmtpPassword` and `Udjat/TelegramBotToken`.
- `string? Read(SecretName)`, `void Write(SecretName, string)`, `void Delete(SecretName)` (the `ISecretStore` interface and
  `SecretName` are in Core, so the save logic can be tested with a fake). The blob is UTF-16.
  Byte arrays are cleared after use. Errors carry the Win32 error code, never the value.
- Elevated Udjat runs as the same user, so it reads that user's vault.
- One integration test writes, reads and deletes a uniquely named credential. It is **skipped unless the
  environment variable `UDJAT_INTEGRATION=1` is set**, so a normal test run never touches Credential Manager.

## Local channels

- **Toast**: `NotifyIcon.ShowBalloonTip` on the UI thread. Windows 10/11 shows it as a notification.
  Title up to 63 characters and text up to 255, cut with "…". The icon is Error for Critical, Warning for
  Warning, Info otherwise. Focus Assist may hide it; that is one reason Critical also sounds an alarm.
- **Sound**: `System.Media.SoundPlayer` with Windows' own `%WINDIR%\Media\Alarm01.wav` (nothing to ship or
  license), played once for Warning and three times for Critical, on a background thread. If the file is
  missing, `SystemSounds.Hand`. A muted PC stays silent; the docs say so.

## Core additions (all unit-tested)

- `LoadResult(Settings, Notice, IsNew)`: `IsNew` is true when there was no file.
- `AppSettings.DismissedNotices`: notice ids the user dismissed forever.
- `SettingsModel`, `SecretField`, `TestState`, `SettingsSaver`, `ISecretStore`: the settings page's logic (what counts as a
  change, the words in the save bar, the save order), with no WPF in it.
- `ReadingHistory(capacity)`: `Add(PollResult)`, `Snapshot(id)`, `Ids`. Old points fall off.
- `StatusSummary.From(PollResult?, bool stopped, bool hasSource)`: returns `(TrayState, Text)`.
- `Notices.Build(...)`: returns the notice list from the CPU status, the load notice, first run, missing
  secrets, the source error and the dismissed ids.

## Build order (each step builds and passes its tests before the next)

This is the order the app was first built in. `LineChart`, the dashboard window and the settings window were later replaced
(docs/UI.md).

1. Rename `Udjat.Sensors.Windows` to `Udjat.Windows` (folders, namespaces, solution, probe, tests, docs).
   All 240 tests still pass.
2. Core additions above, with tests.
3. `SecretStore` (opt-in integration test) and `AutostartTask` (XML tests only).
4. App skeleton: project, manifest, `App`, `SingleInstance`, `AppHost`, `ShellOpen`, tray icon and icons,
   toast and sound channels. Run it once; the owner approves the UAC prompt. Check the tray icon, the
   toast and the sound.
5. Dashboard: `LineChart`, the window and its view model. Run it and take a screenshot for the README.
6. Settings window, including Test buttons and autostart. Run it: save, test a toast, toggle autostart
   (this creates a real scheduled task, so ask the owner first).
7. Docs: README (install, usage, screenshot), ARCHITECTURE, DECISIONS.
