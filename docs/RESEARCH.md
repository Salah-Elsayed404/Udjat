# Dependency research (phase 3)

Checked 2026-10-29 from the pages listed under each item. "Not verified"
means the page did not answer it; do not treat it as a yes.

## Verified

| Item | Finding | Source |
|---|---|---|
| .NET 10 | LTS, released 2025-11-11, supported until 2028-11-14, latest patch 10.0.14. (.NET 8 and 9 both end support 2026-11-10.) | dotnet.microsoft.com/platform/support/policy/dotnet-core |
| LibreHardwareMonitorLib | 0.9.6 (stable), MPL-2.0, ships net10.0 build. Covers Intel/AMD CPUs, NVIDIA/AMD/Intel GPUs, HDD/SSD/NVMe, fans. Some sensors need admin. | nuget.org/packages/LibreHardwareMonitorLib, github.com/LibreHardwareMonitor |
| ScottPlot.WPF | 5.1.59, MIT, net10.0-windows build. **Not used**: replaced by a small custom chart (DECISIONS.md) | nuget.org/packages/ScottPlot.WPF |
| MailKit | 4.18.2 (latest stable), MIT, supports .NET 10. Packages it pulls in, checked in their .nuspec files on restore: MimeKit 4.18.2 (MIT), BouncyCastle.Cryptography 2.7.0 (MIT), System.Security.Cryptography.Pkcs 10.0.13 (MIT). | nuget.org/packages/MailKit; restored package cache |
| RAM usage | Original reasoning: LHM's README does not list memory support, so read RAM with the built-in Win32 call (GlobalMemoryStatusEx), no dependency. **Correction (checked later the same day):** LHM 0.9.6 does have `HardwareType.Memory` and `Computer.IsMemoryEnabled`, so that reasoning was wrong. Re-decided the same day: read RAM from LHM (DECISIONS.md, SENSORS.md). | github.com/LibreHardwareMonitor; metadata of the 0.9.6 DLL |
| LHM package contents | No driver file in the package (searched for .sys, .inf, .cat, .amx, .bin files and anything named pawn). The DLL embeds 12 PawnIO modules as resources: AMDFamily0F, AMDFamily10, AMDFamily17, IntelMSR, IsaBridgeEC, LpcACPIEC, LpcCrOSEC, LpcIO, RyzenSMU, SmbusI801, SmbusNCT6793, SmbusPIIX4. A module is a small program that the PawnIO kernel driver runs. The driver itself is not included, so it must be installed separately, as already decided. The Windows runtime assemblies live under runtimes/win-x64, win-x86 and win-arm64; there is no Linux build. | Restored package, read from metadata only (no code was run) |
| LHM dependency licenses | MPL-2.0: LibreHardwareMonitorLib 0.9.6, DiskInfoToolkit 1.1.2, RAMSPDToolkit-NDD 1.4.2, BlackSharp.Core 1.0.7 (pulled in by those). Apache-2.0: HidSharp 2.6.4 (its nuspec only says "LICENSE.txt"; the file in the package is the Apache License 2.0). MIT (Microsoft): System.Management, System.IO.Ports, System.CodeDom, System.IO.FileSystem.AccessControl, and the runtime.*.native.System.IO.Ports packages (those are for non-Windows systems; expected to drop out of a win-x64 publish, to confirm when publishing). Mono.Posix.NETStandard 1.0.0: the nuspec has no license expression, only a link that leads to the Mono LICENSE file; that file says the class libraries are MIT in general and does not list Mono.Posix among its exceptions, so MIT by inference. | .nuspec files in the restored packages; github.com/mono/mono LICENSE |
| LHM API (0.9.6) | `HardwareType`: Motherboard, SuperIO, Cpu, Memory, GpuNvidia, GpuAmd, GpuIntel, Storage, Network, Cooler, EmbeddedController, Psu, Battery, PowerMonitor. `SensorType`: Voltage, Current, Power, Clock, Temperature, Load, Frequency, Fan, Flow, Control, Level, Factor, Data, SmallData, Throughput, TimeSpan, Timing, Energy, Noise, Conductivity, Humidity. Use: create a `Computer`, switch on only the groups we need (`IsCpuEnabled`, `IsGpuEnabled`, `IsMemoryEnabled`, `IsMotherboardEnabled`, `IsStorageEnabled`; there are more), call `Open()`, then `Update()` each `IHardware` and read its `ISensor` list (`Name`, `Identifier`, `SensorType`, `Value` which can be null). `IHardware` also has `SubHardware`. Which sensor shows up under which hardware (for example fans under the motherboard's SuperIO sub-hardware) is not confirmed; the probe output on a real PC will show it. | Metadata of the DLL |
| Test packages | xunit.v3 4.0.2 (Apache-2.0), xunit.runner.visualstudio 4.0.1 (Apache-2.0), Microsoft.NET.Test.Sdk 18.11.0 (MIT). Every package they pull in was checked in its .nuspec and is MIT or Apache-2.0. Test-only, never shipped. (Checked 2026-10-29.) | api.nuget.org .nuspec files, restored package cache |

## Not verified / open

| Item | Status |
|---|---|
| Licence of the 12 embedded PawnIO modules | They come from the PawnIO.Modules project, which is **LGPL-2.1** (its COPYING file; GitHub reports LGPL-2.1), so shipping LHM also ships LGPL-2.1 binaries. **Accepted by the owner on 2026-10-29**, with conditions (DECISIONS.md). Still not checked: whether LHM's copies are unmodified builds from that repository. |
| Inno Setup license | **Resolved 2026-10-30.** The license text (jrsoftware.org/files/is/license.txt) says "Permission is granted to anyone to use this software for any purpose, including commercial applications", with no fee. Conditions: keep its copyright notice in redistributed binaries (the installer stub does); mark modified versions. The "requested to purchase" on the website is a request for support, not a license term. |

## The driver problem (blocks the "nothing else to install" requirement)

- LHM 0.9.6 release notes mention "Update PawnIO modules to 2.2" and "Fix for new PawnIO release + new installer". So current LHM uses PawnIO.
- PawnIO is a separate kernel driver, installed with its own PawnIO_setup.exe from pawnio.eu. Nothing found says LHM bundles it.
- PawnIO license: GPL-2.0 with a special exception. Redistribution terms are not stated on pawnio.eu ("contact the author before bundling"). Official edition is signed; Unrestricted edition is unsigned.
- The old WinRing0 driver is flagged by Microsoft Defender (VulnerableDriver:WinNT/Winring0). The mixed reports suggest this is why LHM moved to PawnIO.
- What needs a driver: CPU temperature on Intel/AMD (low-level CPU register access), motherboard fans. NVIDIA/AMD/Intel GPU readings normally go through the GPU vendor driver and need no extra kernel driver (not verified for hotspot on each vendor).
- Update (same day): the package was restored and inspected, see "LHM package contents" above. It has no driver file; the PawnIO modules are embedded in the DLL and the driver is not. What still needs a real Windows machine: which sensors work without the driver, and what the app sees when PawnIO is missing (the sensor probe in tools/SensorProbe is for this).

### Consequence
A single installer with zero extra installs is only possible if Udjat's installer
also installs PawnIO (needs author permission or GPL-compliant redistribution)
or if CPU temperature is dropped / made optional. Needs the owner's decision.

### Decision
Option chosen by the owner on 2026-10-29: CPU temperature is optional and requires the user to install PawnIO. Revisit if the author permits bundling.

## Probe results on the owner's PC

Run on 2026-10-29 with `tools/SensorProbe`, once without and once with administrator rights.
PC: MSI PRO B760M-P DDR4, Intel Core i5-13400F, NVIDIA GeForce RTX 3060 (12 GB), one NVMe SSD,
one HDD, about 16 GB RAM. **PawnIO is not installed** (no service, no folder, no uninstall entry),
so this is also the "no extra driver" case that every new user starts in. Untested: AMD CPU,
AMD and Intel GPUs, and anything with PawnIO installed.

**Works as administrator, with no extra driver**

| Our kind | LHM hardware and sensor | Seen |
|---|---|---|
| GPU temp | GpuNvidia, Temperature "GPU Core" | 73 °C |
| GPU hotspot | GpuNvidia, Temperature "GPU Hot Spot" (so NVIDIA does give a hotspot) | 84 °C |
| GPU VRAM | GpuNvidia, SmallData "GPU Memory Used" and "GPU Memory Total" in MB; also Load "GPU Memory" in % | 4886 of 12288 MB, 39.8 % |
| Fans | GpuNvidia, Fan "GPU Fan 1" and "GPU Fan 2" in RPM | about 2060 RPM |
| Drive temp | Storage, NVMe: Temperature "Composite Temperature"; HDD: Temperature "Temperature" | 44 °C, 34 °C |
| RAM | Memory "Total Memory", Load "Memory" in %, Data "Memory Used" and "Memory Available" in GB | 95 % |

**Does not work without PawnIO**
- CPU temperature: all 23 CPU temperature sensors exist, but every one has no value. LHM raises no error and no exception; the value is simply empty.
- Motherboard: the hardware is listed but has no sensors at all, so there are no motherboard fans or temperatures.

**Needs administrator rights** (compared with the run without them): the drives did not appear at all, and the motherboard had no sensors. GPU and RAM read the same either way. CPU temperature had no value in both runs.

**Traps for the sensor mapping**
- Identifiers are not unique: `/gpu-nvidia/0/load/3` is used by both "GPU Bus" and "GPU Memory". Many names repeat (six "D3D Copy" rows).
- An NVMe drive has three live temperatures plus the drive's own limits as "Warning Temperature" (89) and "Critical Temperature" (94). The limits are not readings and must be ignored. They could serve as default thresholds.
- The hardware named "Virtual Memory" has the identifier `/vram`. It is Windows virtual memory, not video memory.
- A missing driver is silent. "CPU sensors present but all empty" is the only sign that PawnIO is missing, so the app must treat it as that.
- The PC was busy with a local-AI style load at the time (RAM 95 % used, GPU 78 to 96 % load, hotspot up to 88 °C). Default thresholds must not fire all the time on a normal load like this.

The full text output is in the session scratch folder (not in the repository, because it names the owner's hardware).
