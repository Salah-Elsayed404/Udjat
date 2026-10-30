// Shows what Udjat can read on this PC: first the raw sensors LibreHardwareMonitor lists, then what
// Udjat makes of them. Run it as administrator (right-click SensorProbe.exe, "Run as administrator").
// It prints to the window and saves the same text to sensor-probe.txt in the folder it runs from.
// It sends nothing anywhere. The text contains hardware model names, no personal data.

using System.Globalization;
using System.Security.Principal;
using LibreHardwareMonitor.Hardware;
using Udjat.Windows.Sensors;

var lines = new List<string>();

void Say(string text)
{
    Console.WriteLine(text);
    lines.Add(text);
}

// In the raw list, only the sensor types Udjat cares about. Run with --all to see everything.
var wanted = new[] { SensorType.Temperature, SensorType.Fan, SensorType.Load, SensorType.Data, SensorType.SmallData };
var showAll = args.Contains("--all");

var isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
Say($"Udjat sensor probe, {DateTime.Now:yyyy-MM-dd HH:mm}");
Say($"{Environment.OSVersion}, running as administrator: {isAdmin}");
if (!isAdmin)
    Say("WARNING: not running as administrator. Most CPU, fan and drive sensors will be missing.");

try
{
    using var source = new LhmSensorSource();

    // Two reads, a moment apart: some sensors only have a value after the second one.
    source.ReadAll();
    Thread.Sleep(1500);
    var readings = source.ReadAll();

    Say("");
    Say("Raw sensors from LibreHardwareMonitor (hardware without any sensor is not listed):");
    PrintRaw(source.LastRaw);

    Say("");
    Say("What Udjat reads:");
    foreach (var reading in readings)
        Say($"  {reading.Kind,-11} {reading.Name,-46} {Format(reading.Value),10}   {reading.Id}");
    Say($"CPU temperature: {source.CpuTemp}");
}
catch (Exception ex)
{
    Say($"FAILED: {ex.GetType().Name}: {ex.Message}");
}

File.WriteAllLines("sensor-probe.txt", lines);
Console.WriteLine($"Saved to {Path.GetFullPath("sensor-probe.txt")}");

if (!Console.IsInputRedirected)
{
    Console.Write("Press any key to close...");
    Console.ReadKey(true);
}

string Format(double? value) => value is { } v ? v.ToString("0.##", CultureInfo.InvariantCulture) : "no value";

void PrintRaw(IEnumerable<RawSensor> raws)
{
    foreach (var hardware in raws.GroupBy(r => r.HardwareId))
    {
        var first = hardware.First();
        Say($"[{first.HardwareType}] {first.HardwareName}   {first.HardwareId}");

        foreach (var sensor in hardware.Where(s => showAll || wanted.Contains(s.SensorType)).OrderBy(s => s.SensorType))
            Say($"  {sensor.SensorType,-12} {sensor.SensorName,-30} {Format(sensor.Value),10}   {sensor.SensorId}");
    }
}
