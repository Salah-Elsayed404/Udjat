# Decisions

A short log of design decisions and why they were made.

| Date | Decision | Reason |
|------|----------|--------|
| 2026-10-29 | Free and open source, published on GitHub | Shared with the local-AI community; portfolio project |
| 2026-10-29 | Windows only for v1 | Focus; sensor layer kept swappable so Linux can follow |
| 2026-10-29 | Support NVIDIA, AMD and Intel GPUs | Local-AI users run all three |
| 2026-10-29 | Only dependencies with commercial-friendly licenses | Keep the project free to use and reuse |
| 2026-10-29 | Curated sensors: CPU temp, GPU temp + hotspot, VRAM, fans, RAM, drive temp | Covers the real risks of long local-AI loads without the noise of full monitors |
| 2026-10-29 | Sensor reading embedded in the app, no third-party install | One install for the user; candidate library LibreHardwareMonitorLib, to verify |
| 2026-10-29 | C# / .NET | Native to LibreHardwareMonitorLib, no bridge layer, single self-contained exe |
| 2026-10-29 | Alert channels: email, sound, Windows notification, Telegram | Cover both at-the-desk and away-from-desk users; each optional |
| 2026-10-29 | Two threshold levels: Warning and Critical | Quiet early warning avoids alert fatigue; loud alarm only when action is needed |
| 2026-10-29 | Optional auto-protect: kill allow-listed processes on sustained Critical | Protects unattended overnight runs; off by default and allow-list only for safety |
| 2026-10-29 | Log alerts only, not every reading | Useful record without a growing data file |
| 2026-10-29 | Dashboard with live charts + tray icon | Clear at-a-glance status; strong portfolio piece. Charts in memory only, consistent with alerts-only logging |
| 2026-10-29 | Autostart as a setting, on by default, minimized to tray | A watchdog is only useful if it is running; user can turn it off |
| 2026-10-29 | Ship as a Windows installer via GitHub Releases | App needs admin and an autostart task; installer handles both and uninstalls cleanly |
| 2026-10-29 | MIT license for Udjat | Simple, permissive, standard for portfolio projects |
| 2026-10-29 | Secrets in Windows Credential Manager | Encrypted per user; nothing sensitive in plain-text files |
| 2026-10-29 | WPF on .NET 10, built-in APIs first, few packages | Smaller attack surface for an elevated app; easier to review |
| 2026-10-29 | Monitor logic isolated from UI and network | Unit-testable core |
| 2026-10-29 | RAM read via built-in Win32 call, not LHM (superseded later the same day, see below) | LHM does not document memory support; fewer moving parts |
| 2026-10-29 | CPU temperature optional; needs PawnIO installed separately by the user | PawnIO is a separate GPL-2.0 kernel driver with unstated redistribution terms; bundling it is not safe until confirmed with its author. All other sensors work with one install |
| 2026-10-29 | Monitoring core in its own class library, `Udjat.Core` (net10.0) | The compiler then enforces "no UI or network in the core"; tests run without WPF; a Linux port can reuse it |
| 2026-10-29 | Class named `SensorMonitor`, not `Monitor` | `Monitor` clashes with `System.Threading.Monitor`, which is imported by default |
| 2026-10-29 | Thresholds set per sensor kind; state tracked per individual sensor | One setting per kind is simpler for non-technical users; two drives still alert independently |
| 2026-10-29 | Fan speeds shown, not alerted | Many GPUs stop their fans at idle, so low-RPM alerts would be false alarms; a failed fan shows up as rising temperature, which is alerted |
| 2026-10-29 | VRAM and RAM thresholds in percent used | The same setting works for any GPU or RAM size |
| 2026-10-29 | A level rises after 2 consecutive readings and falls with a 3-unit hysteresis | Single spikes and values hovering at a limit would otherwise cause alert fatigue |
| 2026-10-29 | Cooldown per sensor and per level | Escalation to Critical is never silenced by a recent Warning |
| 2026-10-29 | "Recovered" notice only for episodes whose alert was sent | A silenced alert followed by a "recovered" message would be confusing |
| 2026-10-29 | Auto-protect trigger fires once per Critical stay | Avoids a kill loop if the user restarts their AI app while the hardware is still hot |
| 2026-10-29 | Time via the built-in `TimeProvider` | Tests control the clock with no extra package |
| 2026-10-29 | Settings are checked by `Validate()` methods that the `ThresholdState` and `SensorMonitor` constructors call | Records stay plain; a bad setting fails at startup with `ArgumentException`, not during a poll |
| 2026-10-29 | `SensorMonitor` calls each event subscriber separately and reports a throwing one through `PollFailed` | A bug in one handler (UI, email) must never block an alert to another channel or end the poll loop |
| 2026-10-29 | Tests run on xunit.v3 with the Microsoft.Testing.Platform runner, selected in `global.json` | `dotnet test` on the .NET 10 SDK refuses the old VSTest mode. Command: `dotnet test --solution Udjat.slnx` |
| 2026-10-29 | Shared build settings in `Directory.Build.props`; warnings are errors | One place for net10.0 and nullable; the code stays warning-free from the first line |
| 2026-10-29 | The 300-line limit applies to test files too; tests are split by topic with shared helpers in their own files | Keeps every file reviewable, as the project principles require |
| 2026-10-29 | A small diagnostic, `tools/SensorProbe`, prints the sensors LibreHardwareMonitor sees; the sensor-source design waits for its output | Sensor names and availability differ per PC and per vendor, and cannot be read from documentation. Not shipped; may become a diagnostic feature later |
| 2026-10-29 | Accept the 12 LGPL-2.1 PawnIO modules embedded in LibreHardwareMonitorLib (owner's decision) | LGPL is not GPL and allows commercial use; the alternative is writing sensor code per GPU vendor. Conditions: ship the LHM DLL unmodified, as a separate file next to the exe (publish as a folder, not a single-file exe), and list the LGPL-2.1 terms and the modules' source in THIRD-PARTY-NOTICES |
| 2026-10-29 | Sensor reading in its own library, `Udjat.Sensors.Windows` | It is the only project that touches LHM and its dependencies, so it is easy to audit in an elevated app; the tests reach it without WPF; a Linux source can sit beside it later |
| 2026-10-29 | A thin `LhmSensorSource` plus a pure `SensorMapper` working on flattened `RawSensor` records | The mapping rules are where the bugs hide (the probe found several name traps), and they must be testable without real hardware |
| 2026-10-29 | RAM read from LHM ("Total Memory", Load "Memory"); replaces the Win32 decision above | The earlier premise was wrong. The probe shows LHM reads RAM with no admin rights and no driver. One source and no P/Invoke code |
| 2026-10-29 | NVMe drives get their own kind, `NvmeTemp`, separate from other drives | NVMe SSDs run much hotter than hard disks by design; one threshold for both would either nag about the SSD or miss an overheating hard disk |
| 2026-10-29 | VRAM % computed from "GPU Memory Used" and "GPU Memory Total", not from the Load "GPU Memory" sensor | That sensor's LHM id clashes with "GPU Bus"; integrated GPUs without a total get no VRAM reading instead of a misleading one |
| 2026-10-29 | Reading ids built as `{hardware id}/{kind}` (fans keep the LHM id) | LHM's own ids are not always unique |
| 2026-10-29 | "CPU sensors present but all empty" means the driver is missing (or Udjat is not elevated) | LHM reports a missing driver silently; this is the only reliable sign |
| 2026-10-29 | Only five LHM groups enabled (CPU, GPU, memory, storage, motherboard); read-only; never fan control | Least code running with administrator rights |
| 2026-10-29 | Storage updated at most every 30 s | Drive temperatures change slowly; fewer SMART queries to the disks |
| 2026-10-29 | After installing PawnIO the user restarts Udjat | LHM looks for the driver only when it opens; live re-detection is not worth the code in v1 |
| 2026-10-29 | `Udjat.Sensors.Windows` targets plain `net10.0` | The platform analyzer raised no warning for the LHM calls, so the `-windows` target is not needed. The tests can reference it and run without hardware |
| 2026-10-29 | The probe uses the real `LhmSensorSource` and prints both the raw list and "what Udjat reads" | One run on a tester's PC checks the mapping rules against real hardware, not just the raw names |
| 2026-10-29 | Settings, alert routing, the alert log and ProcessGuard live in `Udjat.Core`, not in the WPF app | They are pure logic or plain .NET, so they can be unit-tested without WPF; the core still has no UI, network or Windows API |
| 2026-10-29 | Email and Telegram channels in a new library, `Udjat.Channels` | Network code stays out of the core but can still be tested (fake HTTP handler, message building) |
| 2026-10-29 | Default thresholds: CPU 90/97, GPU 84/90, GPU hotspot 100/108, RAM 97/99 %, drives 50/55, NVMe 75/82 (°C unless shown) | A normal heavy AI load must stay quiet (the owner's PC: RAM 95 %, hotspot up to 88 °C). Starting points, several marked [verify]; reasons in ALERTS.md |
| 2026-10-29 | VRAM alerts off by default | Model runners fill VRAM on purpose, so a full VRAM is normal for local AI |
| 2026-10-29 | "Recovered" goes to the channels that heard the episode's highest alert (was: toast only) | A user who got a Critical alert on Telegram away from the PC needs to hear it is over |
| 2026-10-29 | Sound plays only for Warning and Critical alerts | An alarm for information messages would just be noise |
| 2026-10-29 | "Monitoring stopped" after 60 s without a successful poll, checked by its own timer, not by counting `PollFailed` | A blind watchdog must say so. `PollFailed` also fires for handler bugs, and a hung sensor read raises nothing at all |
| 2026-10-29 | No delivery retries in v1; each failure is logged with a redacted reason | Keeps the dispatcher simple; failures stay visible in the alert log |
| 2026-10-29 | ProcessGuard closes exact name matches only; a built-in deny list wins; child processes are not touched | Only processes on the user's allow-list may ever be killed, and never a system process |
| 2026-10-29 | Messages in English for v1, all built in one class (`AlertText`) | One file to translate later |
| 2026-10-29 | Settings saved through a temporary file; an invalid file is renamed and defaults are used | Never a half-written settings file, never a crash on a bad one |
| 2026-10-29 | The Telegram token is checked against a strict pattern before use | It goes into a URL path |
| 2026-10-29 | Settings are read by laying the user's JSON over the defaults; comments and trailing commas are allowed | A missing field then takes its default at every level, even inside one threshold, and hand-edited files are forgiving |
| 2026-10-29 | `AutoProtector` is the small piece that connects `CriticalSustained` to ProcessGuard and the dispatcher | ProcessGuard stays a pure rule set; the wiring is separate and tested alone |
| 2026-10-29 | `SystemProcessTable.Kill` re-checks the process name just before closing it | Windows can give a process id to another program after the list was made |
| 2026-10-29 | The email channel is proven TLS-only by a test with a fake mail server that refuses encryption | The claim "nothing is sent unencrypted" is checked, not assumed. Weakening the option to "when available" makes the test fail |
| 2026-10-29 | "Recovered" goes to the channels the highest alert was routed to, whether or not each delivery worked | Keeps the rule simple and predictable |
| 2026-10-29 | The delivery queue holds 200 messages and drops the oldest when full | A long network outage must not use up memory |
| 2026-10-29 | The "Test" button sends are not written to the alert log | The log is for real alerts only |
| 2026-10-29 | The app's manifest asks for administrator rights (`requireAdministrator`) | The sensors need them; there is then no half-working mode to explain. The autostart task avoids the UAC prompt at logon |
| 2026-10-29 | Links and files are opened through `explorer.exe` | An elevated app must never start a browser or editor with administrator rights |
| 2026-10-29 | Autostart is a Task Scheduler task defined as XML, and the app reconciles it at every start | The `schtasks` defaults would stop Udjat after 72 hours and skip it on battery. A moved or reinstalled Udjat fixes its own task; the installer only deletes it |
| 2026-10-29 | `Udjat.Sensors.Windows` becomes `Udjat.Windows`: sensors, secrets and autostart | One Windows platform layer that the tests can reach without WPF; a Linux port would add `Udjat.Linux` beside it |
| 2026-10-29 | What the UI shows (overall status, notices, chart history) is logic in `Udjat.Core/Status` | It can be unit-tested; the WPF code only draws it |
| 2026-10-29 | A small custom line chart instead of ScottPlot (replaces the stack choice) | ScottPlot brings SkiaSharp and about 10 MB of native code into an elevated process, for two simple charts |
| 2026-10-29 | One instance only (named mutex); a second start shows the first one's dashboard | Two watchdogs would send every alert twice |
| 2026-10-29 | The sensor source is opened once; a settings change rebuilds the rest of the pipeline | Opening LHM is slow, and the chart history should survive a settings change |
| 2026-10-29 | The "monitoring stopped" check is reset after the PC wakes from sleep | Otherwise every wake-up would send "Udjat cannot read the sensors" |
| 2026-10-29 | Closing the dashboard hides it to the tray; only Exit in the tray menu stops Udjat | A watchdog closed by accident is worse than none |
| 2026-10-29 | The Test button uses a throwaway dispatcher built from the values on screen | The user can try a channel before saving it; timeouts and secret hiding are reused, nothing is logged |
| 2026-10-29 | Password and token boxes never show a stored value; they show "Saved" and offer "Remove" | A secret never appears on screen |
| 2026-10-29 | The Credential Manager test runs only with `UDJAT_INTEGRATION=1` | A normal test run must not touch the user's credential store |
| 2026-10-29 | The sound is Windows' own Alarm01.wav: once for Warning, three times for Critical | Nothing to ship or license |
| 2026-10-29 | Light theme for v1, with all colours in one resource dictionary (replaced the same day, see below) | Keeps v1 small; a dark theme is a later addition in one place |
| 2026-10-29 | Dashboard redesign agreed with the owner from a mockup: a panel per device (processor, graphics card, memory, storage), load beside temperature | The owner's request; each part reads on its own at a glance (docs/UI.md) |
| 2026-10-29 | CPU and GPU load added as display-only readings | The owner wants load beside temperature. High load alone is not a hardware danger (100 % is normal for local AI), so it never alerts |
| 2026-10-29 | Dark theme by default, with a dark/light switch in the header | The owner's request. One set of colour keys in two resource dictionaries, switched at run time |
| 2026-10-29 | `SensorReading` gets `Device` and `TotalGb` | Panels group readings by device, and memory and VRAM show used / total GB |
| 2026-10-29 | GPU fans become their own kind, `GpuFan` | Fans sit in the panel of their device; motherboard fans appear now that PawnIO is installed |
| 2026-10-29 | A small 30-minute sparkline per panel replaces the two large charts; `LineChart` is deleted | The owner's choice; less drawing code, and each part has its own history |
| 2026-10-29 | The neon Eye of Horus is our own vector drawing; the tray uses a simplified eye in the status colour | Our own artwork avoids copyright questions; a 16-pixel tray icon cannot show a glow |
| 2026-10-29 | The look is inspired by gaming hardware tools, not copied | No vendor's design, logo or artwork is reused |
| 2026-10-30 | One window with an icon side menu: Overview, Fans, theme, Settings | The owner asked for a Fans tab and a side menu with icons; the Overview stays one screen as the requirements say |
| 2026-10-30 | Settings is a page in the main window, not a modal window | Same theme and navigation as the rest; the form survives switching pages, with a save bar that appears only after a change |
| 2026-10-30 | Stopped fans are chips, not cards; only turning fans get a card with a chart | A motherboard has many empty fan headers; a card each would bury the useful ones |
| 2026-10-30 | The theme button lives only in the side menu | One place to change it; the settings page has no theme field |
| 2026-10-30 | Navigation items are `RadioButton`s, icons are our own stroked paths | Keyboard and screen-reader behaviour for free; same glow as the eye, no icon package |
| 2026-10-30 | Every page of the window and every settings tab stays in the window; only the chosen one is visible | A page that is taken out of the window cannot find its commands, so its buttons stayed disabled until the next key or mouse event |
| 2026-10-30 | The settings page keeps its own form (`SettingsModel`) with its own data context | The window's content is rebuilt every poll; unsaved edits must survive that, switching pages, and closing the window |
| 2026-10-30 | Save order: check the values, autostart task, secrets, settings file, restart the pipeline | A failure in an early step leaves nothing half done; the problem is shown in the save bar |
| 2026-10-30 | The Test buttons send from what is on screen, even unsaved, and even when the channel's switch is off | You can check an email setup before you turn it on; nothing is saved or logged |
| 2026-10-30 | The dashboard restores itself with `ShowWindow` when Windows says it is minimized | A minimized window stayed minimized after a show request while WPF believed it was normal; opening from the tray must always work |
| 2026-10-30 | Own scroll bar, text boxes, switches and buttons in `Themes/Inputs.xaml` | The default WPF controls ignore the dark theme (white boxes, a light scroll bar) |
| 2026-10-30 | The installer is built with Inno Setup 6 | Its license (read from jrsoftware.org/files/is/license.txt) allows use for any purpose, including commercial, with no fee; the only condition is keeping its copyright notice in what is redistributed, which the installer does itself |
| 2026-10-30 | The app is published self-contained, as a folder, not as one single-file exe | The LGPL-2.1 PawnIO modules live inside LibreHardwareMonitorLib.dll, which must ship unmodified and as a separate file (see the LGPL decision); a single-file exe would pack it inside. Self-contained means the user installs no .NET (about 170 MB before the installer compresses it) |
| 2026-10-30 | The installer does not create the "start with Windows" task; the app does, on its first run | One owner of that task (`AutostartTask`), which also fixes it when the exe moves; the installer only deletes it on uninstall |
| 2026-10-30 | Uninstall asks whether to delete settings, the alert log and the saved passwords (default No; a silent uninstall never deletes them) | They belong to the user; a reinstall should find them again |
| 2026-10-30 | The installer closes a running Udjat with `Udjat.exe --exit` and waits 3 seconds | Udjat hides its window instead of closing it, so the usual "close the application" step cannot do it |
| 2026-10-30 | The program icon is generated by `tools/make-icon.ps1` from the eye drawing; 16 to 32 px use the simplified eye | Same drawing as the window, nothing to keep in step by hand; fine lines only blur at small sizes |
| 2026-10-30 | The version number lives only in `src/Udjat/Udjat.csproj`; the build script passes it to the installer | One place to change it |
| 2026-10-30 | Releases are not code-signed yet | A certificate costs money and needs identity checks; SmartScreen may warn until one is bought. Revisit before a public release |
| 2026-10-30 | The installer always installs to Program Files (no folder page; a `/DIR=` outside it is refused) | The "start with Windows" task runs Udjat with full rights and no prompt; it must only point at a folder standard users cannot change (security review S1) |
| 2026-10-30 | `AutostartTask` creates the task only when the exe is in Program Files, deletes one that points elsewhere, and leaves one that points inside alone | Otherwise a copy run from Downloads or a build folder would hijack the installed program's task, or hand administrator rights to anyone who can replace that exe |
| 2026-10-30 | Auto-protect closes only processes of the same Windows session and account as Udjat | Udjat is administrator and the list comes from a file; without this the file could make it close services or other people's programs (S3). Ollama and LM Studio run as the user, so the real use case works |
| 2026-10-30 | Settings and the alert log live in `%ProgramData%\Udjat`, made by Udjat with permissions only administrators can change | Udjat writes as administrator; in `%AppData%` a normal program could swap the folder for a junction and make Udjat overwrite a system file (S2). Everyone can still read the log |
| 2026-10-30 | An existing data folder that is not safe is renamed aside and replaced, never repaired in place; if that fails Udjat does not start | A folder a normal user created first stays theirs (they can always change its permissions), so only a new one can be trusted |
| 2026-10-30 | The old `%AppData%` settings are not migrated | 0.1.0 is unreleased, and an administrator reading a file any program could have changed is the weakness being removed |
| 2026-10-30 | `SingleInstance` refuses objects another program created first, and says so | A same-user program could create `Local\Udjat.*` first and make Udjat quit silently (S4). A real Udjat makes both events, then the mutex, so any other mix is someone else's |
| 2026-10-30 | The settings page's logic (`SettingsModel`, `SettingsSaver`, `ISecretStore`) is in `Udjat.Core`, not in the WPF project | It writes secrets and the autostart task, so it needs tests; the WPF project is not testable. Core stays free of UI: the page converts `CanRemove` to a visibility itself |
| 2026-10-30 | The installer's "Start Udjat now" runs with `runascurrentuser` | Found by the owner running the real wizard: by default Setup starts that program as the ordinary user, and Udjat requires administrator rights, so it failed with error 740. The silent test could not catch it because it skips that step |
| 2026-10-30 | The window icon comes from the exe (`EyeArt.cs` deleted); the installer build leaves out `.pdb` files | Less code and a smaller install for the same result |
| 2026-10-30 | The program icon is the Eye of Horus in gold with depth on a lapis-blue tile with a gold rim (replaces the cyan neon eye) | The owner wanted a real Pharaonic eye in gold. Five styles, then six versions of one, were shown as pictures and the owner chose "lapis and gold, more 3D". The drawing was redone as filled shapes (brow, lids with the kohl line, iris, teardrop, spiral), which reads as the hieroglyph where thin lines did not |
| 2026-10-30 | Tray icons draw the same eye flat in the status colour; they read the shapes from the Shared.xaml resources | The colour must keep saying the status, and depth only blurs at 16 px; one copy of the shapes in the window (plus the icon script) keeps them in step |
| 2026-10-30 | Charts on the Overview have a grid, numbers on both sides and a time axis; `ChartScale` in Core picks the round numbers | The owner said the bare lines were not understandable. The scale logic is pure so it is tested; the Fans page's small cards keep the bare line |
| 2026-10-30 | Celsius or Fahrenheit is a display setting: the file, the monitor and the limits stay in Celsius | Changing a unit must never change what a limit means, and the monitor and the tests need one unit. Switching the unit converts the boxes on the Alerts tab; saving converts them back, rounded to three places |
| 2026-10-30 | No Pharaonic background pattern yet | Four ideas were shown; the owner said to leave it and come back later |
| 2026-10-30 | Udjat is published as a public GitHub repository (Salah-Elsayed404/Udjat); commits use the GitHub noreply email; `AGENTS.md`, an old copy of `CLAUDE.md`, was removed | Open source and a portfolio project from the start. The real email would be readable by anyone from the history. A stale copy of the project notes could mislead a reader; `CLAUDE.md` is the one that is kept up to date |
| 2026-10-30 | Before publishing, the project was scanned for secrets and personal data (none found); the sample Telegram token in the tests was replaced by an obviously fake one | A real-looking token in a public repository can trigger secret scanners and confuse readers, and git history cannot be cleaned afterwards without rewriting it |

