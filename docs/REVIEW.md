# Security and code review

Date: 2026-10-30. Version reviewed: 0.1.0 (the code and the installer as built that day). Reviewer: Claude (Opus), for the owner.
**Status: every finding below has been fixed (2026-10-30, Sonnet); "What was done" at the end says how.**

## How it was done

- Read every piece of code that runs with administrator rights and touches something outside the process: the autostart
  task, files, processes, secrets, the network channels, the single-instance objects, the installer script.
- Checked the Windows facts the findings depend on, on the owner's PC (`icacls C:\`, `icacls "C:\Program Files"`).
- Asked NuGet for known vulnerabilities: `dotnet list Udjat.slnx package --vulnerable --include-transitive` found none.
- Code quality: test coverage of the newest code, the 300-line rule, and whether the docs still match the code.

Who could attack, from most to least likely:

1. **A program running as the same user, without admin rights.** Udjat always runs as admin, so anything it does with
   files, processes or tasks on behalf of that user's settings is a way up. Microsoft does not treat UAC as a strict
   security boundary, but a program that adds an easy, silent way up is still a problem for an "attack surface minimal" app.
2. **Another account on the same PC.** If it can change what Udjat runs, it gets the admin's rights. This is a real boundary.
3. The network: the mail server and api.telegram.org.

Severity: **High** = another account, or any program of the user, can get admin rights silently and easily.
**Medium** = needs a program already running as the user, and some skill. **Low** = annoyance or denial of service.

## Summary

| Id | Severity | Finding | Effort to fix |
|---|---|---|---|
| S1 | High | The "start with Windows" task can run an exe that other accounts can replace | Small |
| S2 | Medium | Admin-rights file writes in the user's own folders can be redirected | Medium |
| S3 | Medium | The settings file can make Udjat close any program on the PC, as admin | Small |
| S4 | Low | Another program can stop Udjat from starting, or tell it to quit | Small |
| S5 | Low | A Test button can stay stuck on "Sending…" | Tiny |
| S6 | Info | "Start with Windows" does not work for a standard user who elevates with an admin password | Docs |
| Q1 | Quality | The settings page's logic has no automated tests | Medium |
| Q2 | Quality | docs/APP.md and docs/ARCHITECTURE.md say things the code no longer does | Small |
| Q3 | Quality | Three files are close to the 300-line rule | Later |
| Q4 | Quality | Small simplifications: the drawn window icon, .pdb files in the installer | Tiny |

## S1 (High): the autostart task can run an exe that other accounts can replace

**Where.** `AutostartTask.Reconcile`, called at every start by `AppHost.ReconcileAutostart`. "Start with Windows" is on by
default, and Reconcile points the task at whatever `Udjat.exe` is running now. The installer's folder page lets the user
pick any folder.

**Why it matters.** The task runs at logon with the highest rights and no prompt. Program Files can only be changed by admins,
but other places cannot be trusted. Checked on the owner's PC: the root of `C:\` gives *Authenticated Users* **Modify** on
every folder created under it (`NT AUTHORITY\Authenticated Users:(OI)(CI)(IO)(M)`), and a build folder or Downloads belongs
to the user.

**Example.** Udjat is installed to `C:\Udjat` (or run once from Downloads, or from `D:\Udjat\src\...\bin`). Another account on
the PC, or a program running as the user, replaces `Udjat.exe` or any of the about 270 DLLs next to it. At the admin's next
logon Windows starts it with full admin rights and no prompt.

**Fix.**
1. Installer: `DisableDirPage=yes`, so it always installs to Program Files.
2. App: create or repoint the task only when the exe is under `%ProgramFiles%`. Otherwise do not touch an existing task,
   and show a notice: "Start with Windows works when Udjat is installed in Program Files." Put the check behind a function
   that `AutostartTask` receives, so it is unit-tested with the existing fake scheduler.

## S2 (Medium): admin-rights file writes in the user's own folders can be redirected

**Where.** `SettingsStore.Save` (writes `settings.json.tmp`, then moves it over `settings.json`), `SettingsStore.UseDefaultsInstead`
(moves the file with overwrite), `AlertLog.Append` and `RotateIfFull` (appends, then moves with overwrite), and
`ShellOpen.Path` (creates a folder). All of them are in `%AppData%\Udjat` and `%LocalAppData%\Udjat`, which any program
running as the user can change.

**Why it matters.** A program without admin rights can replace those folders with a junction to an object-manager symbolic link
(neither needs a privilege), so that `settings.json.tmp` or `alerts.old.log` points at a system file. When Udjat, as admin,
saves the settings or rotates the log, it overwrites or deletes that file. This is a well-known way up from "a program running
as the user" to admin.

**Fix.** Keep Udjat's files in `%ProgramData%\Udjat`, created by Udjat with an ACL of SYSTEM and Administrators full control,
Users read. Udjat is always elevated, so it can write there; people can still read the log. Read the old `%AppData%` settings
file once to carry the user's settings over, without moving or deleting anything there with admin rights. Update the paths in
the README, the General tab text and docs/ALERTS.md. This also closes the way into S3.

## S3 (Medium): the settings file can make Udjat close any program on the PC

**Where.** `AutoProtector`, `ProcessGuard` and `SystemProcessTable`. Udjat closes every process whose name is on the list, in
every session and under every account, with admin rights. Only a short list of Windows programs is protected. The list, the
switch and the limits come from `settings.json` (S2), and `Validate` accepts any Critical limit, for example RAM at 1 %, so
auto-protect can be made to fire within a minute.

**Example.** A program running as the user turns auto-protect on, lists the name of a security tool's helper service, a backup
service or another user's programs, and sets the RAM Critical limit to 1 %. A minute later Udjat closes them as admin, which
that program could not do itself.

**Fix.** Close only processes that run in the user's own session and belong to the user: `Process.SessionId` equal to Udjat's
session, and the process owner equal to the logged-on user. Ollama, LM Studio and similar apps run that way; services and other
users' programs are then never touched. Add the session and owner to `ProcessInfo`, so the rule is tested with the fake table.
Together with S2 this closes the hole from both sides.

## S4 (Low): another program can stop Udjat from starting, or tell it to quit

**Where.** `SingleInstance`: the mutex and the two events have fixed names in the session's `Local\` namespace, and the events
are opened without checking whether Udjat created them.

**Example.** A program running as the user creates `Local\Udjat.SingleInstance` first. At logon Udjat thinks it is already running
and exits without a word. Or it creates `Local\Udjat.Exit` first and sets it later, and Udjat quits. Monitoring stops with no
sign. (Windows' integrity labels already stop such a program from signalling events that Udjat itself created.)

**Fix.** Check `createdNew` for both events when Udjat is the first instance, and refuse ones that already existed. When
the mutex says "already running" but nothing answers the show request within a few seconds, say so in a balloon instead of
exiting silently.

## S5 (Low): a Test button can stay stuck on "Sending…"

`SettingsController.TestAsync` catches only `ArgumentException`. Anything else leaves the test state busy, and the button stays
disabled until Udjat restarts. Nothing else is expected to throw there today (the dispatcher catches channel errors), but a
`try/finally` that always finishes the state costs nothing.

## S6 (Info): autostart for a standard user who elevates with an admin password

The task runs with the standard user's rights, and Udjat requires admin, so it does not start at logon. Say so in the README and
in the General tab's text, or skip creating the task when the user is not an admin.

## Q1: the settings page's logic has no automated tests

`SettingsModel` (what counts as a change, when the save bar shows "Saved", the secret states) and `SettingsController` (the save
order, Test with the values on screen) live in the WPF project, which the test project does not reference. They were checked by
hand on the running app, which is not enough for code that writes secrets and the autostart task.
**Fix:** move the WPF-free parts to Udjat.Core (a `SecretField` without `Visibility`, the save bar rules), and put an
`ISecretStore` interface in front of `SecretStore` so the save order can be tested with fakes, like `ITaskScheduler`.

## Q2: docs that no longer match the code

Documentation is a deliverable, so these count:
- docs/APP.md says autostart uses `schtasks /Create` (it uses the COM Task Scheduler), that `MonitorHealth.Reset()` is called
  after sleep (the code uses a sleep guard), and that `TaskScheduler.UnobservedTaskException` is observed (nothing hooks it;
  harmless in .NET, but the sentence is false). Its file list still names `LineChart`, `DashboardViewModel` and `SettingsWindow`.
- docs/ARCHITECTURE.md's dependency table still names `LineChart`.

## Q3: files close to the 300-line rule

`AppHost.cs` (291), `SensorMapperTests.cs` (295), `DashboardModel.cs` (269). The next feature that touches `AppHost` should move
something out of it first (for example the notices and tray wiring).

## Q4: small simplifications

- `EyeArt` draws the window icon at run time, but the exe now carries the same icon and a WPF window uses it by default.
  Deleting `EyeArt.cs` removes 43 lines (check the taskbar afterwards).
- The installer ships `.pdb` debug files. Harmless and small, but `DebugType none` for the published build keeps the install minimal.

## What is already good

- Email is TLS only (STARTTLS required, or TLS from the first byte) and MailKit checks the server certificate.
- The Telegram token is checked against a strict pattern before it goes into a URL, never appears in an error, and anything
  shaped like a token is masked in logs. Messages are plain text (no `parse_mode`), so hardware names cannot inject formatting.
- Secrets live only in Credential Manager; the byte buffers are wiped; the settings page never reads a secret back.
- Links and folders are opened through `explorer.exe`, so the browser does not run as admin, and only `https` links are accepted.
- The autostart task is registered through the COM Task Scheduler from a string, with no temporary XML file to swap.
- P/Invoke declarations load their DLLs from System32 only. LibreHardwareMonitor runs only the five sensor groups Udjat needs.
- Auto-protect is off by default, matches names exactly, never closes children, and has a deny-list and a PID re-check.
- No known vulnerable package (NuGet check above), and the build treats warnings, including NuGet audit warnings, as errors.

## Suggested order of the fixes (a Sonnet round)

1. S1: installer `DisableDirPage=yes`; trusted-location check in `AutostartTask` with tests; the notice.
2. S3: session and owner filter in `ProcessGuard` with tests.
3. S2: move settings and the log to `%ProgramData%\Udjat` with an ACL; carry the old settings over; docs.
4. S5 and S4; then Q1 (tests for the settings logic).
5. Q2 docs; Q4 tidy-ups. Rebuild the installer and repeat the install test.

## What was done

Fixed in the order above. 419 automated tests pass (55 more than before the fixes); the new rules each have tests.
The installer was rebuilt and tested on the owner's PC the same day (40 checks, all passed): the folder made by the elevated app is
owned by Administrators, only SYSTEM and Administrators can change it, Users can read it, nothing was written to `%AppData%`, the
task was made because the app ran from Program Files, and the window icon comes from the exe.

| Id | What changed |
|---|---|
| S1 | The installer has no folder page and refuses a `/DIR=` outside Program Files. `AutostartTask` takes a trusted folder (Program Files): outside it, no task is made, one that points outside is deleted, one that points inside is left alone, and the dashboard shows a notice (Save in Settings adds a note to the "Saved" message). Tests: `AutostartTaskTests`, `StatusTests`. |
| S3 | `IProcessTable` knows `Me` and `OwnerOf`; `ProcessGuard` closes only processes in Udjat's own session and account, and reports the others as "left alone" in the auto-protect message. `SystemProcessTable` moved to `Udjat.Windows` (it asks Windows for the session and the account SID) and re-checks the owner before closing. Tests: `ProcessGuardTests`, `AutoProtectTests`. |
| S2 | New `DataFolder` (`Udjat.Windows/Storage`): settings and log live in `%ProgramData%\Udjat`, created with its own permissions (SYSTEM and administrators full control, everyone else read only). An existing folder that is a junction, owned by someone else or loosely protected is renamed aside and replaced; if that fails Udjat does not start and says why. `ShellOpen` no longer creates folders. The uninstaller deletes only this folder. **Not done, on purpose:** carrying over the old `%AppData%\Udjat\settings.json`. Version 0.1.0 has not been released, and reading a file that any program could have changed, as an administrator, is the thing this fix avoids; the only existing settings are the owner's test ones (delete the old folders by hand). Tests: `DataFolderTests` (including a real junction). |
| S4 | `SingleInstance` moved to `Udjat.Windows`. It checks that a real Udjat's objects are all new or all old (the events are made before the mutex); anything else means another program made them first, and Udjat says so in a message box (never for `--exit`, which the installer waits for). Tests: `SingleInstanceTests`. |
| S5 | `TestAsync` has a `finally` that always finishes the Test button. |
| S6 | Documented in the README, the General tab and docs/APP.md. |
| Q1 | The settings page's logic moved to `Udjat.Core` and has tests: `SettingsModel`, `SecretField`, `TestState`, and the new `SettingsSaver` (the save order, with `ISecretStore` and fakes). `SecretName` moved to Core with the interface. Side effect, an improvement: both secrets are length-checked before anything is written. Tests: `SettingsModelTests`, `SettingsSaverTests`. |
| Q2 | docs/APP.md, ALERTS.md and ARCHITECTURE.md now match the code (COM Task Scheduler, the sleep guard, no `LineChart`, the new folders and paths). |
| Q3 | `AppHost.cs` is 294 lines, still under the limit but with no room: move the notices and tray wiring out of it before adding anything there. |
| Q4 | `EyeArt.cs` deleted (the window uses the exe's icon); the installer build publishes without `.pdb` files. |

