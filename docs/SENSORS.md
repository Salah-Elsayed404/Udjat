# Windows sensor source

Spec for the sensor code in `src/Udjat.Windows/Sensors` (the project was called Udjat.Sensors.Windows until the app step). It turns LibreHardwareMonitor (LHM) readings into the
`SensorReading`s that the Monitoring core understands. The evidence behind each rule is in
RESEARCH.md, "Probe results on the owner's PC". Rules marked **[verify]** follow LHM's naming
but have not been seen on real hardware yet; testers confirm them with `tools/SensorProbe`.

**Status: built.** The mapping rules are covered by `SensorMapperTests` (25 tests, including the
owner's PC as the probe listed it). `LhmSensorSource` was checked with the probe on the owner's PC
(Intel CPU, NVIDIA GPU, NVMe + HDD, no PawnIO), with and without administrator rights: it reads
RAM, GPU temperature, hotspot, VRAM, both GPU fans and both drives, and reports CPU temperature as
`NoValues`. Not yet seen on real hardware: CPU temperature with PawnIO installed, AMD CPUs, and AMD
or Intel GPUs.

## Shape

```
LHM Computer --> LhmSensorSource --> RawSensor list --> SensorMapper --> SensorReadings + CpuTempStatus
                 (thin; needs real hardware)            (pure; unit-tested)
```

| File | Role |
|---|---|
| `Udjat.Windows.csproj` | Class library, `net10.0`; the files below are in its `Sensors/` folder. References Udjat.Core and LibreHardwareMonitorLib 0.9.6. The only project that touches LHM. |
| `RawSensor.cs` | `record RawSensor(HardwareType HardwareType, string HardwareId, string HardwareName, SensorType SensorType, string SensorId, string SensorName, double? Value)`. One LHM sensor, flattened into plain data so tests can build it by hand. |
| `CpuTempStatus.cs` | `Available`, `NoValues`, `NotFound` (see below). |
| `SensorMapper.cs` | `static SensorMap Map(IReadOnlyList<RawSensor> raws)` returning `SensorMap(IReadOnlyList<SensorReading> Readings, CpuTempStatus CpuTemp)`. Every rule below lives here. |
| `LhmSensorSource.cs` | `ISensorSource, IDisposable`. Opens LHM, updates it on each `ReadAll()`, flattens the tree into `RawSensor`s and calls the mapper. Exposes `CpuTemp` and `LastRaw` from the last read (for diagnostics and the probe). |

The platform analyzer (CA1416) does not object to the LHM calls, so the library stays on plain `net10.0`.

## Opening LHM

- Enable only `IsCpuEnabled`, `IsGpuEnabled`, `IsMemoryEnabled`, `IsStorageEnabled` and
  `IsMotherboardEnabled` (motherboard fans, once PawnIO is installed). Everything else stays off
  (network, controllers, PSU, battery, power monitor): less code running in an elevated process.
- Read only. Never use `ISensor.Control` or any other fan-control API.
- The constructor calls `Open()`. If that throws, the app starts without sensors and says so.
- LHM looks for the PawnIO driver only when it opens. After installing PawnIO the user restarts
  Udjat, and the app says so. No live re-detection in v1.
- `Dispose()` calls `Close()`. One caller at a time (the monitor's poll loop); not thread-safe.

## ReadAll()

1. Update every hardware item in `Computer.Hardware`, and its sub-hardware, recursively. Read the
   list again on each call, because LHM adds and removes hardware (a USB drive, for example).
2. Storage hardware is updated at most every 30 s and keeps its last values in between. Drive
   temperatures change slowly, and this means fewer SMART queries. Whether frequent SMART reads keep
   idle hard disks from spinning down is **[verify]**.
3. If updating one hardware item throws, skip only that item for this poll. If every item failed,
   rethrow the first exception; the monitor reports it through `PollFailed`.
4. Flatten into `RawSensor`s, sub-hardware included with its own type and id. LHM's `float?` values
   become `double?`. Keep the list as `LastRaw`.
5. Return `SensorMapper.Map(raws).Readings`, and keep its `CpuTemp`.

## Mapping rules (SensorMapper)

Names are LHM's own English strings; LHM does not translate them. Matching is exact and
case-insensitive. A sensor with no value is skipped, so it is absent, as the core expects.

| Kind | Hardware | Rule | Reading id |
|---|---|---|---|
| CpuTemp | Cpu | The first of these Temperature names that has a value: "CPU Package", "Core (Tctl/Tdie)" **[verify]**, "Core (Tdie)" **[verify]**, "Core (Tctl)" **[verify]**, "Core Max". If none of them has a value but other CPU temperatures do, use the highest of those, ignoring every name that contains "Distance to TjMax" (those count down to the limit; they are not temperatures). | `{hw}/cpu-temp` |
| GpuTemp | GpuNvidia, GpuAmd **[verify]**, GpuIntel **[verify]** | Temperature "GPU Core" | `{hw}/gpu-temp` |
| GpuHotspot | same | Temperature "GPU Hot Spot" | `{hw}/gpu-hotspot` |
| GpuVram | same | SmallData "GPU Memory Used" divided by "GPU Memory Total", times 100, only when Total > 0. Not the Load "GPU Memory" sensor: its LHM id clashes with "GPU Bus". Integrated GPUs without a Total get no VRAM reading. | `{hw}/vram` |
| Ram | Memory whose id is `/ram` | Load "Memory" (% used). Ignore `/vram`: that is Windows virtual memory, not video memory. | `{hw}/ram` |
| NvmeTemp | Storage whose id starts with `/nvme/` | Temperature "Composite Temperature", else "Temperature" | `{hw}/drive-temp` |
| DriveTemp | Any other Storage (hard disks; SATA SSDs **[verify]**) | Temperature "Temperature", else "Composite Temperature" | `{hw}/drive-temp` |
| CpuLoad | Cpu | Load "CPU Total" (%). Display only. | `{hw}/cpu-load` |
| GpuLoad | same as the GPU rows | Load "GPU Core" (%). Display only. Not "GPU Memory" or "GPU Bus". | `{hw}/gpu-load` |
| GpuFan | GPU hardware | Every Fan sensor (RPM). Display only. | The LHM sensor id |
| Fan | Any other hardware (the motherboard's SuperIO, coolers) | Every Fan sensor (RPM). Display only. | The LHM sensor id |

`{hw}` is the LHM hardware id, for example `/gpu-nvidia/0`.

Always ignored: a drive's "Warning Temperature" and "Critical Temperature" (the drive's own limits,
not readings), "Temperature #n" (secondary drive sensors), and every sensor type not listed above.

Ids: there is one reading per hardware item per kind (fans aside), so `{hw}/{suffix}` cannot clash
even though LHM's own ids sometimes do. If two readings still end up with the same id, keep the first.

Names (display):
- Most readings use the hardware name, for example "NVIDIA GeForce RTX 3060".
- Fans use LHM's own sensor name, for example "GPU Fan 1". The device says where the fan is.
- RAM uses "System memory".

Every reading also carries `Device` (the hardware name) so the dashboard can group readings into one panel per device, and the
two percentage readings that are a share of something carry `TotalGb`:
- Ram: "Memory Used" + "Memory Available" (Data, GB), when both exist.
- GpuVram: "GPU Memory Total" (SmallData, MB) divided by 1024.

The UI adds the kind label ("GPU hotspot", ...).

## CPU temperature status

| Status | Meaning | What the app tells the user |
|---|---|---|
| Available | A CpuTemp reading was produced | Nothing |
| NoValues | CPU temperature sensors exist but all are empty | Not running as administrator: "run Udjat as administrator". As administrator: "CPU temperature needs the free PawnIO driver (link). Install it, then restart Udjat." |
| NotFound | No CPU temperature sensors at all | "CPU temperature is not supported on this CPU." |

LHM reports a missing driver silently (empty values, no error), so this is the only way to detect
it. The administrator check lives in the app, not here.

## Tests

`SensorMapperTests` build `RawSensor` lists by hand:
- The owner's PC exactly as the probe saw it, with PawnIO missing: the expected readings
  (GPU temp 73, hotspot 84.25, VRAM 4886 of 12288 MB, two GPU fans, NVMe 44, HDD 34, RAM 95.25)
  and status NoValues.
- The same PC with CPU values present: CpuTemp from "CPU Package"; status Available.
- AMD-style CPU names; fallback to the hottest CPU temperature; "Distance to TjMax" ignored.
- No CPU temperature sensors at all: status NotFound.
- A GPU without a hotspot; VRAM with Total 0 or missing; an integrated GPU.
- NVMe limits ignored; NVMe versus other drives; virtual memory ignored.
- Duplicate ids keep the first; sensors with no value skipped; fans from sub-hardware.

`LhmSensorSource` needs real hardware. It is checked by running the probe, which prints both the raw
LHM list and the mapped readings ("what Udjat sees").

## The probe

`tools/SensorProbe` references `Udjat.Windows`, not LHM directly. It creates an
`LhmSensorSource`, calls `ReadAll()` twice 1.5 s apart, then prints:
1. `LastRaw`, limited to the sensor types Udjat cares about (`--all` for everything). Hardware
   without any sensor, like the owner's motherboard without PawnIO, is not listed;
2. "What Udjat reads": kind, name and value of each mapped reading;
3. the CPU temperature status.
