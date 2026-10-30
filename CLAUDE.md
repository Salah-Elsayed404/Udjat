# Udjat

Lightweight desktop hardware monitor for people running local AI models.
Watches only the essential sensors (CPU temp, GPU temp, ...) and alerts
(email + local sound) when a threshold is crossed.

## Principles (non-negotiable)
- Small and simple: few files, no file over ~300 lines.
- Clean, readable structure the owner (Salah) can review.
- Minimal attack surface: few dependencies, no unnecessary privileges.
- Every dependency must have a license that allows commercial use
  (MIT / BSD / Apache-2.0 preferred; check anything else before adding; no GPL).
  Accepted exception (owner, 2026-10-29): the LGPL-2.1 PawnIO modules embedded in
  LibreHardwareMonitorLib. Ship that DLL unmodified and separate, with notices.
- No secrets in plain-text files.

## Open source and documentation
- Udjat is open source and will be published on GitHub as a portfolio project.
- Documentation is a first-class deliverable from day one, not an afterthought:
  - README.md stays current with every change (what, why, install, usage, screenshots).
  - docs/DECISIONS.md records every significant decision with its reason.
  - Every dependency is listed with its license in README.md.
- Code must be presentable: clear names, short functions, docstrings, no dead code.

## Prior art
- A working prototype exists: HWiNFO "Run a Program" -> Python script that
  sends email via Gmail SMTP with a per-sensor cooldown.

## Requirements (filled in during Q&A)
- Audience: free, shared with other people (not just the owner). Settings and UI must be usable by non-technical users.
- OS: Windows only for v1. Keep sensor reading behind a small interface so Linux can be added later.
- GPUs: NVIDIA, AMD and Intel. (Implies a sensor backend that covers all three, not just NVML.)
- Sensors (fixed, curated set): CPU temp, GPU temp, GPU hotspot temp, GPU VRAM usage, fan speeds, system RAM usage, drive temperature. No long sensor lists. Also shown, never alerted (owner, 2026-10-29): CPU load and GPU load.
- DECISION (2026-10-29): CPU temperature is an OPTIONAL feature that needs the PawnIO driver, installed by the user separately (link + clear in-app guidance). Everything else (GPU temp/hotspot/VRAM, fans where available, RAM, drive temp) works with no extra install. Do NOT bundle or silently install PawnIO unless its license/redistribution is confirmed with the author. The app must detect the missing driver and explain it, never crash.
- Sensor source: fully embedded. The user installs Udjat only, nothing else (no HWiNFO). Candidate: LibreHardwareMonitorLib (MPL-2.0) bundled in the app; requires admin + its driver. To verify in phase 3.
- Language: C# / .NET (same stack as the sensor library; single self-contained exe). Owner is learning, so keep code plain and well-commented in English.
- Alert channels: email (SMTP), local sound alarm, Windows toast notification, Telegram bot. Each channel is optional and independently configurable.
- Thresholds: two levels per sensor, Warning and Critical. The user picks which channels fire for each level (default: Warning = Windows toast; Critical = all channels).
- Auto-protect (optional, OFF by default): if a sensor stays above Critical for a configurable time, terminate user-chosen processes (e.g. ollama, LM Studio) and notify the user. Only processes on the user's explicit allow-list may ever be killed.
- Logging: alerts and auto-protect actions only (timestamp, sensor, value, level, channels). No continuous sensor history. Keep the file small (rotate).
- UI: tray icon (color reflects status) + dashboard window with live charts. Charts use an in-memory rolling window (e.g. last 30-60 min), not a history file. Keep the dashboard to one screen; it is a portfolio showcase but must not bloat the code.
  Redesign agreed 2026-10-29 (docs/UI.md, mockup docs/ui-mockup.html): a panel per device with load beside temperature, dark theme by default with a dark/light switch, neon Eye of Horus icon. Inspired by gaming hardware tools, never copied.
  Pages agreed 2026-10-30 (docs/UI.md "Pages", mockup docs/ui-pages-mockup.html): one window with an icon side menu (Overview, Fans, theme, Settings). The Overview stays one screen; Fans and Settings are pages beside it, and the settings window of docs/APP.md becomes a page.
- Autostart: setting, ON by default, starts minimized to tray. Because sensor reading needs admin, implement via a Task Scheduler task with highest privileges (no UAC prompt on each boot), not the Run registry key. Verify in phase 2.
- Distribution: a standard Windows installer (sets up autostart task, clean uninstall). Installer tool must also be free for commercial use. Published via GitHub Releases.
- Project license: MIT. Bundled third-party libraries keep their own licenses (e.g. MPL-2.0 for LibreHardwareMonitorLib); list them in README and THIRD-PARTY-NOTICES.
- Secrets (SMTP password, Telegram bot token): Windows Credential Manager only. Never in settings.json, logs, or exceptions.

## Architecture
See docs/ARCHITECTURE.md (draft). Items marked [verify] must be confirmed before code depends on them.

## Build and test
- `dotnet build Udjat.slnx` and `dotnet test --solution Udjat.slnx` (run from the repo root).
- Build and run the tests after every step. Warnings are errors.
- Status: `src/Udjat.Core` (Monitoring core) and `src/Udjat.Windows` (sensor source, docs/SENSORS.md) are done and tested (75 tests). `tools/SensorProbe` shows what Udjat reads on a PC; run it elevated (it needs administrator rights, so ask the owner before launching it). Settings, alerts, auto-protect and the email and Telegram channels are built and tested (240 tests; docs/ALERTS.md). The WPF app's skeleton (tray, toast, sound, single instance, --exit, --data-dir), secrets and autostart are built and tested (317 tests, 2 opt-in); the main window is built (docs/UI.md): side menu, Overview, Fans, Settings (364 tests, 2 opt-in), both themes. Packaging is written (docs/DECISIONS.md, 2026-10-30): the .ico, THIRD-PARTY-NOTICES and licenses, a self-contained publish that was smoke-tested, installer/Udjat.iss and tools/build-installer.ps1; the installer is compiled (artifacts/Udjat-Setup-0.1.0.exe, 52.6 MB, Inno Setup 6.7.3, already installed per-user on the owner's PC) and its silent install, first run (the app makes the scheduled task), install over a running app, and silent uninstall were tested on the owner's PC on 2026-10-30 (30 checks passed, machine left as it was). Not tested: the installer's wizard pages and the uninstall question about settings and passwords (both need a person at the screen). The Opus security and code review is done (docs/REVIEW.md, 2026-10-30) and every finding is fixed (419 tests, 2 opt-in): autostart only from Program Files (S1), auto-protect closes only the user's own programs (S3), settings and log in %ProgramData%\Udjat with administrator-only permissions (S2), single-instance squatting (S4), the Test button (S5), the settings page's logic moved to Core with tests (Q1). The installer was rebuilt and retested on the owner's PC on 2026-10-30 (40 checks passed, machine left as it was): the app, elevated, made C:\ProgramData\Udjat owned by Administrators with SYSTEM and Administrators full control and Users read only, wrote nothing to AppData, made its task only because it ran from Program Files, and the window icon comes from the exe. The old %AppData% settings are not migrated. The owner then ran the real wizard: its "Start Udjat now" step failed with error 740 (Setup started Udjat without administrator rights); fixed with the runascurrentuser flag and the installer rebuilt (docs/DECISIONS.md), waiting for the owner to run the new one. Still not tested: the rest of the wizard, the uninstall question (need a person), real email and Telegram, and the Sound test. After that the owner asked for three changes (2026-10-30, 459 tests): the gold lapis Eye of Horus icon (window, tray, .ico; docs/UI.md), charts on a grid with axis numbers and time (`ChartScale`), and a Celsius or Fahrenheit setting. Built and photographed in the running app (Celsius and Fahrenheit, dark and light), not yet in a rebuilt installer; the README screenshots are still the old look. The Pharaonic background pattern was postponed by the owner. Next: rebuild the installer, retest it, refresh the screenshots, then decide on code signing. Running the app needs the owner to approve a UAC prompt; turning on autostart creates a real scheduled task, so ask first.

- Published (2026-10-30): https://github.com/Salah-Elsayed404/Udjat, public, branch main. Commits use the GitHub noreply email, not the owner's real one. `artifacts/` is git-ignored; the installer goes on GitHub as a Release asset, never in the repository.

## Plan
1. Requirements Q&A (Opus) - done
2. Architecture + sensor-reading approach (Opus) - draft done
3. Verify libraries and licenses (Sonnet) - done; driver decision made (see Requirements), details in docs/RESEARCH.md
4. Build (Sonnet)
5. Packaging + autostart (Sonnet)
6. Security and code review (Opus)
