# Dashboard redesign: panels, dark and light themes, neon eye

Agreed with the owner on 2026-10-29 from the mockup in `docs/ui-mockup.html` (open it in a browser).
It replaces the "Dashboard" section of docs/APP.md: the tiles and the two big charts go; the rest of
APP.md (tray, start and stop, settings window, secrets) still holds. Status: built (the dashboard, both themes, the gauges, the neon eye and the tray icons). Differences from this spec are listed at the end.

## What the owner asked for

- Load (%) next to temperature, with each part on its own: processor, graphics card, memory.
- A modern look in the style of gaming hardware tools: dark, neon accents, gauges. Inspired by that
  style, not copying any vendor's design, logo or artwork.
- Dark by default, with a button to switch between dark and light.
- An Eye of Horus icon in a neon style.
- Answers to the open questions: a storage panel, fans inside the panel of their device, a small chart
  inside each panel, and load shown only (no load alerts).

## Data changes (Udjat.Core and Udjat.Windows)

New sensor kinds, all **display only** (no thresholds, never alerted, like fans):

| Kind | From LHM | Unit | Reading id |
|---|---|---|---|
| `CpuLoad` | Cpu, Load "CPU Total" | % | `{hw}/cpu-load` |
| `GpuLoad` | GpuNvidia / GpuAmd **[verify]** / GpuIntel **[verify]**, Load "GPU Core" | % | `{hw}/gpu-load` |
| `GpuFan` | Fan sensors on GPU hardware (they were `Fan` until now) | RPM | the LHM sensor id |

`Fan` now means every other fan (the motherboard's SuperIO chip, coolers), which shows up now that the
owner has installed PawnIO.

`SensorReading` gets two optional fields (defaults keep today's code compiling):
- `Device`: the hardware it belongs to, for example "NVIDIA GeForce RTX 3060". The dashboard groups by it
  (two graphics cards give two panels). The mapper sets it for every reading.
- `TotalGb`: for the percentage readings, the size they are a share of, so the dashboard can say
  "10.3 / 12 GB". RAM: "Memory Used" + "Memory Available" (Data, GB) on `/ram`. VRAM: "GPU Memory Total"
  (SmallData, MB) / 1024. Null for everything else.

Fan names become the sensor's own name ("GPU Fan 1"), because the device is now in `Device`. Every
other `Name` stays as it is, so alert wording does not change.

`AppSettings.Theme`: `Dark` (default) or `Light`.

## Panels (DashboardModel)

`DashboardModel.Panels(PollResult, AppSettings)` replaces `Tiles`. Pure logic, unit-tested. It returns one
`PanelInfo` per device, in this order:

| Panel | One per | Gauge (ring) | Big number | Also shows | Chart (last 30 min) |
|---|---|---|---|---|---|
| Processor | CPU | `CpuLoad` | `CpuTemp`, coloured by level, with its limits | Motherboard fans (`Fan`), if any | CPU temperature + load |
| Graphics card | GPU | `GpuLoad` | `GpuTemp` and, smaller, `GpuHotspot` | VRAM bar "used / total GB · %", GPU fans | GPU temperature + load |
| Memory | (one) | `Ram` % | Used GB | "Warning 97% · Critical 99%" | RAM % |
| Storage | (one, all drives) | none | none | One row per drive: name, type (NVMe / HDD), temperature with a bar and its limits | none (drive temperatures change slowly) |

- A value that is missing shows "—", never a zero. CPU temperature without PawnIO is "—", and the
  notice explains it.
- The level colour of a temperature, or of RAM, follows its alert level: normal, warning, critical.
  The ring itself always uses the accent colour, because load is never an alarm.
- Limits text as today: "Warning 90 · Critical 97", or "No alert".
- A panel with no readings at all is not shown (for example, no drives when Udjat is not elevated).

## Window layout

- **Header**: the neon eye, "Udjat", the status pill (the `StatusSummary` text in its colour),
  "Watching N sensors · every 2 s", then on the right a theme button (sun or moon) and a settings button.
- **Notices** under the header, as today, styled for the theme.
- **Panels** in a two-column grid that wraps. Minimum window 760 × 600; it opens at 1000 × 760.
- The window's title bar follows the theme (`DwmSetWindowAttribute`, `DWMWA_USE_IMMERSIVE_DARK_MODE`, through
  a small P/Invoke), so a dark window does not get a white title bar.

## Themes

- Two resource dictionaries with the same keys: `Themes/Dark.xaml` and `Themes/Light.xaml`. All windows
  use `DynamicResource`, so switching replaces one merged dictionary and everything updates at once.
- The theme button and the settings window both change `AppSettings.Theme`; it is saved at once.
- The settings window (docs/APP.md) is built with these resources from the start.

| Key | Dark | Light |
|---|---|---|
| WindowBackground | #0B0E13 | #F6F7F9 |
| PanelBackground | #121821 | #FFFFFF |
| PanelBorder | #1F2A37 | #E1E4E8 |
| TextPrimary | #E6EDF3 | #1F2328 |
| TextSecondary | #7D8590 | #656D76 |
| Accent (rings, load lines) | #22D3EE | #0891B2 |
| Track (empty part of rings and bars) | #1F2A37 | #E5E9EF |
| Normal | #3FB950 | #1A7F37 |
| Warning | #E3B341 | #9A6700 |
| Critical | #F85149 | #CF222E |
| Vram (bar) | #A78BFA | #7C3AED |
| NoticeBackground / NoticeBorder | #2B2111 / #6E5313 | #FFF6DD / #E8C766 |

The light colours are darker versions of the dark ones, so text and lines keep enough contrast on white.

## Controls (src/Udjat/Dashboard)

- `RingGauge`: a small control that draws a track circle, an arc for 0–100 %, and the value in the middle.
  About 60 lines.
- `Sparkline`: the 30-minute line chart, one or two series. It **replaces `LineChart`**, which is deleted. Since 2026-10-30 it
  can sit on a grid (below).
- Panels are `DataTemplate`s in the window's XAML; no other new control.
- Keep within the project rules: no file over about 300 lines.

## The eye (changed 2026-10-30: gold, with depth, instead of cyan neon)

The owner found the first icon too plain and asked for a real Pharaonic Eye of Horus in gold. Five styles and then six versions
of the chosen one were drawn and shown as pictures; the owner picked "lapis and gold, more 3D" (docs/DECISIONS.md).

- One vector drawing in a 120 x 96 box, kept as `Geometry` resources in Themes/Shared.xaml (`EyeShapes`, `EyeSpiral`,
  `EyePupil`): the brow, the upper lid that runs out into the long kohl line, the lower lid, the teardrop and the spiral
  (the parts of the classic wedjat), and the iris. The symbol is ancient; any ready-made drawing of it belongs to its artist,
  so none is copied.
- The look: gold with depth (a dark copy under the shape, a light copy over it, the gold gradient on top, a soft shadow) on a
  lapis-blue rounded tile with a gold rim, a glazed shine and a few gold flecks. The same in both themes (`EyeImage`,
  `LapisBrush`, `EyeGoldBrush`, `EyeShadow` in Shared.xaml).
- The side menu shows it on a 46 px tile. The program's `.ico` (16 to 256 pixels) is made from the same shapes by
  `tools/make-icon.ps1`.
- **Tray icon**: at 16 pixels depth and gradients only blur, so the tray draws the same shapes flat, in the status colour (green,
  amber, red, grey) with a thin outline, on a dark disc. `TrayIcons` reads the shapes from the Shared.xaml resources.
- Not done: a Pharaonic pattern in the window background. Four ideas were shown (a frieze along the bottom, a tile, a frame,
  big corner motifs) and the owner will come back to it.

## Charts on a grid (added 2026-10-30)

The first sparkline had no axes, so the lines said nothing about the numbers. A panel's chart now has:
- a grid, four gaps each way, in the faint `ChartGridBrush`;
- the numbers of the first line on the left and of the second on the right, each in its line's colour (the temperature on the
  left, the load 0 to 100 on the right);
- the time along the bottom ("-30 min", "-15 min", "now"; seconds while the history is young);
- a dot at the latest value.
`ChartScale` (Core, tested) picks the range so the grid lines carry round numbers (steps of 1, 2, 2.5, 5, 10; a fan's speed also
1.2 to 8) and keeps a small wiggle small (at least 20 degrees, 36 in Fahrenheit). The Fans page's small cards keep the bare line.
Axes are drawn only when the chart is at least 160 x 80 pixels.

## Celsius or Fahrenheit (added 2026-10-30)

A setting on the General tab (`temperatureUnit` in the file). Readings, limits and the file stay in Celsius; only what is shown
changes: the dashboard values and limits, the chart's axis, alert messages and the log, the tray tooltip, and the limits typed on
the Alerts tab. Switching the unit converts the boxes on that tab (90 °C becomes 194 °F), so the limits keep their meaning, and
saving converts them back (rounded to three places). Percentages and fan speeds never change.

## Build order (Sonnet; each step builds and passes its tests first)

1. Close the running Udjat (`Udjat.exe --exit`; the owner approves the UAC prompt).
2. Core: `SensorReading.Device` and `TotalGb`; the kinds `CpuLoad`, `GpuLoad`, `GpuFan` with labels and units;
   `AppSettings.Theme`; `DashboardModel.Panels` replacing `Tiles`, with tests (the tile tests are rewritten).
3. Windows: the new mapping rules, `Device` and `TotalGb` in the mapper; update the owner's-PC fixture and tests.
4. App: the two themes and switching, the dark title bar, `RingGauge`, `Sparkline` (delete `LineChart`), the
   new window layout, the header buttons, the neon eye and the new tray icons. Run it (UAC) and send the owner
   a screenshot of each theme.
5. Then the settings window (docs/APP.md), using the themes from the start.
6. Docs: README (features, screenshots), ARCHITECTURE, DECISIONS, SENSORS (new rules), CLAUDE.md.

## Differences between this spec and the code

Settled while building:

- The theme type is `AppTheme`, not `ThemeMode`, because WPF already has a `System.Windows.ThemeMode`.
- The sparkline's time axis fits the history there is (up to 30 minutes, at least 10 seconds), so a young chart fills its
  width. Temperature lines are never zoomed in further than 20 degrees, so small wiggles stay small.
- A panel's small chart takes the height that is left, so the panels in a row line up.
- Fans: the ones turning are listed with their speed, the stopped ones are only counted ("CPU Fan 857 RPM · 4 stopped"),
  because a motherboard has many empty fan headers.
- The settings button in the header is not there yet; it comes with the settings window.
- The window's title bar follows the theme through `DwmSetWindowAttribute` (Windows 11 and recent Windows 10).
- Screenshots for the README are taken by a script that runs elevated, asks the window to draw itself, and presses the theme
  button through UI Automation. Capturing a region of the screen is not used: it would include whatever is in front.

# Pages: side menu, Fans and Settings

Status: built on 2026-10-30 (the side menu, the Overview, the Fans page and the Settings page, both themes). Mockup: `docs/ui-pages-mockup.html` (add `#overview`, `#fans`
or `#settings` to the address to see one window).

The owner asked for a Fans tab and for the tabs to be a side menu with icons. The Overview stays one screen, as the
requirements ask; the Fans and Settings pages sit beside it in the same window. This **replaces the modal settings window**
of docs/APP.md: its four tabs and their content stay, but as a page.

## Window

- `DashboardWindow` becomes `MainWindow`: a side menu on the left, then the top bar, the notices and the selected page.
  It opens at 1080 × 760, minimum 840 × 600.
- **Side menu** (76 px wide, `RailBackgroundBrush`, a 1 px border on its right):
  - top: the neon eye (42 × 27) with "UDJAT" under it (10 px, secondary colour; WPF has no letter spacing, so the
    letters are separated by thin spaces, U+2009);
  - pages: **Overview**, **Fans**; then a gap; then **theme** (sun and "Light" in the dark theme, moon and "Dark" in the
    light one) and **Settings**;
  - each page item is a `RadioButton` (group "Pages") with the style `NavItem`: icon (22 px) above its label (10.5 px).
    Radio buttons give the arrow keys, the checked state and screen-reader names for free;
  - checked: `AccentSoftBrush` background, icon and label in `AccentBrightBrush`, the icon drawn twice (a 5 px faint stroke
    under a 1.7 px one, like the eye), and a neon bar on the menu's left edge (3 × 28 bright, over 7 × 32 faint);
  - hover: `ButtonHoverBrush`;
  - Settings shows a 7 px dot in `WarningBrush` while the settings page has unsaved changes.
- **Top bar**: the page title (20 px, semibold), the status pill (as today), and "Watching N sensors · every 2 s" on the
  right. The theme button moves from here to the side menu.
- **Notices** stay under the top bar, on every page.
- The tray menu gets "Settings…", which opens the window on the Settings page.

New theme keys (both dictionaries):

| Key | Dark | Light |
|---|---|---|
| RailBackground | #0F141B | #EDF0F3 |
| AccentSoft (checked item, chips) | #1A22D3EE | #170891B2 |
| AccentBorder (badges, checked tab) | #155E6B | #9BD3E0 |

## Icons (24 × 24, stroked, no fill; `Shared.xaml`)

Drawn by us, as paths, so the checked item can glow the same way as the eye.

| Key | Path data |
|---|---|
| NavOverview | `M4,4 H10 V10 H4 Z M14,4 H20 V10 H14 Z M4,14 H10 V20 H4 Z M14,14 H20 V20 H14 Z` |
| NavFan | A `GeometryGroup`: the ring `EllipseGeometry` centre 12,12 radius 10; the blade `M11.2,10 C9.6,7.4 10.4,4.6 13,4.4 C15.6,4.2 16.4,7.4 13.6,10.6 Z` three times, with `RotateTransform` 0, 120 and 240 around 12,12; the hub `EllipseGeometry` radius 1.6 |
| NavSun | Ellipse radius 4 at 12,12, plus `M12,2.5 V5 M12,19 V21.5 M2.5,12 H5 M19,12 H21.5 M5.3,5.3 L7,7 M17,17 L18.7,18.7 M5.3,18.7 L7,17 M17,7 L18.7,5.3` |
| NavMoon | `M20,14.5 A8.5,8.5 0 1 1 9.5,4 A7,7 0 0 0 20,14.5 Z` (check it on screen; adjust the inner arc if the crescent looks too thin) |
| NavGear | `M 10.08,4.85 L 10.64,2.30 L 13.36,2.30 L 13.92,4.85 L 15.70,5.59 L 17.90,4.17 L 19.83,6.10 L 18.41,8.30 L 19.15,10.08 L 21.70,10.64 L 21.70,13.36 L 19.15,13.92 L 18.41,15.70 L 19.83,17.90 L 17.90,19.83 L 15.70,18.41 L 13.92,19.15 L 13.36,21.70 L 10.64,21.70 L 10.08,19.15 L 8.30,18.41 L 6.10,19.83 L 4.17,17.90 L 5.59,15.70 L 4.85,13.92 L 2.30,13.36 L 2.30,10.64 L 4.85,10.08 L 5.59,8.30 L 4.17,6.10 L 6.10,4.17 L 8.30,5.59 Z`, plus an ellipse radius 3.2 at 12,12 |

## Overview page

The panels as they are now, moved from the window into `OverviewPage`. One addition: the fans line in the Processor and
Graphics card panels ends with a "Fans ›" link that opens the Fans page.

## Fans page

Pure logic in Core, `Status/FanModel.cs`, unit-tested: `FanModel.Groups(PollResult, ReadingHistory)` returns:

- `FanGroup(Title, Subtitle, CountText, Fans, Stopped, Note)`, one per device: first every graphics card ("Graphics card",
  from `GpuFan`), then every other device ("Motherboard", from `Fan`). The subtitle is the device name.
- `CountText`: "2 turning", "2 turning · 6 stopped" or "2 stopped".
- `Fans`: the fans turning now (value above 0), by name: `FanInfo(Id, Name, RpmText, RangeText)`.
  `RangeText` comes from the history: "Last 30 min · 830 – 910 RPM", or "Last 30 min · 860 RPM" when it did not change.
- `Stopped`: the names of the fans at 0 RPM, shown as small chips after the label "Stopped or not connected".
  A stopped fan has no card: a motherboard has many empty fan headers, and a card each would bury the real ones.
- `Note`: for a graphics card whose fans are all stopped, "Graphics cards often stop their fans while they are cool.
  This is normal." Otherwise null.
- No fans at all: the page says "No fans found. Graphics card fans show up by themselves; motherboard fans need the free
  PawnIO driver."

RPM numbers get a thousands separator ("1,460 RPM"), here and in the Overview's fans line.

Layout: a scrolling list of groups. A group is a header row (title 15 px semibold, subtitle 12 px, count on the right),
then the cards in three equal columns, then the chips. A card: the fan icon (18 px, accent), the name, a "Turning"
badge, the speed (30 px semibold, "RPM" smaller), a sparkline and the range text. The sparkline gets a third role,
`Speed`, drawn in the accent colour from 0 up to the highest point (at least 1,000 RPM), so a steady fan is a steady
line and a fan that starts shows the climb. Fans are display-only: no levels, no alert colours.

## Settings page

- The four tabs of docs/APP.md (Alerts, Channels, Auto-protect, General) as a row of `RadioButton`s with the style
  `SubTab`. Each tab is its own `UserControl` file, so none passes about 300 lines.
- The content of each tab is as APP.md says, with the existing `SettingsForm` as the data. The theme is not a setting
  on this page; the side menu button is the one place to change it.
- Alerts tab, as in the mockup: one row per kind (sensor, a switch, Warning and Critical boxes with the unit); a row that
  is off is dimmed; GPU memory carries the hint "full is normal for local AI"; then "Repeat the same alert after N
  minutes" and a "Reset to defaults" link that resets only this tab.
- **Save bar** at the bottom of the page, shown only when something changed: "You have unsaved changes", Discard, Save.
  A validation error replaces the text, in `CriticalBrush`, and nothing is saved.
- "Changed" is decided in Core: `SettingsForm.Differs(SettingsForm other)` compares every field (tested). A secret typed
  in a password box also counts as a change.
- **Save** turns the form into settings with `ToSettings(current)`, where `current` is read at the moment of Save, so a
  theme switch or a dismissed notice made in the meantime is kept. Then secrets, autostart and `AppHost.Rebuild`, as
  APP.md says, and the form is rebuilt from the saved settings.
- The unsaved form survives switching pages and closing the window (closing only hides it); Exit discards it.

Shared control styles in `Themes/Controls.xaml`: `NavItem`, `SubTab`, `Switch` (a `ToggleButton`), text box,
`PrimaryButton`, `SecondaryButton`, `LinkButton`, `Chip`, `Badge`.

## Files

```
src/Udjat/
  Dashboard/   MainWindow.xaml(.cs), MainHost.cs, MainView.cs (renamed from Dashboard*), OverviewPage.xaml,
               PanelTemplates.xaml, RingGauge.cs, Sparkline.cs
  Fans/        FansPage.xaml(.cs)
  Settings/    SettingsPage.xaml(.cs), AlertsTab.xaml, ChannelsTab.xaml(.cs), ProtectTab.xaml, GeneralTab.xaml(.cs)
  Themes/      Controls.xaml (new), Shared.xaml (icons added), Dark.xaml, Light.xaml (new keys)
src/Udjat.Core/Status/FanModel.cs (new); Settings/SettingsForm.cs (Differs)
```

## Build order (Sonnet; each step builds and passes its tests first)

0. When the owner approves: set the status above to "agreed", add the decisions to docs/DECISIONS.md (side menu with
   pages, the Overview still one screen; settings as a page instead of a modal window; stopped fans as chips, not cards;
   the theme only in the side menu; the unsaved form kept across pages), and update the UI line in CLAUDE.md.
1. Close the running Udjat (`Udjat.exe --exit`; the owner approves the UAC prompt).
2. Core: `FanModel` with tests; RPM with a thousands separator; `SettingsForm.Differs` with tests.
3. Shell: `MainWindow` with the side menu, top bar and notices; the Overview page; the theme button in the menu; the
   "Fans ›" link. Run it and send the owner a screenshot.
4. Fans page with the `Speed` sparkline. Run it and send a screenshot.
5. Settings page: the tab row and the save bar, then Alerts, Channels (with the Test buttons), Auto-protect, General.
   Run it: change a limit and save, send a test notification. Turning autostart on creates a real scheduled task, so
   ask the owner first.
6. Docs: README (features, screenshots), APP.md (the settings window is now a page), ARCHITECTURE, DECISIONS, CLAUDE.md.

### Differences between the Pages spec and the code

- The side menu items are `RadioButton`s only for the pages; the theme switch is a `Button` with the same look (`NavAction`).
- All the pages and all four settings tabs are children of the window all the time and only the chosen one is visible, instead of
  swapping the content: a page that left the window could not find its commands, and its buttons stayed disabled.
- The settings page binds to a `SettingsModel`, not to the window's `MainView`. The password boxes are tied to the model through
  `PasswordBinder`, so the page can clear them after saving.
- Buttons ask for things through routed commands (`SettingsCommands`), which the page handles; a tab is layout only.
- The "Saved. Udjat is using the new settings." message stays in the save bar until the next real edit.
- The tray menu has "Settings…", which opens the window on the Settings page.
- Not done: the Auto-protect tab does not warn while you type that a name is a protected system program. Save checks that each name is
  well formed; a protected program is skipped when auto-protect runs, and the alert says so.
