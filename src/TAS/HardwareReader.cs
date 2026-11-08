using LibreHardwareMonitor.Hardware;

namespace TAS;

public readonly record struct SensorValue(string Name, float Value);

/// <summary>
/// Any sensor reading, with its type ("Temperature", "Load", "Fan", ...) so custom alerts can target it.
/// </summary>
public readonly record struct SensorReading(string Name, string Type, float Value);

internal static class SensorInfo
{
    public static string UnitFor(string type) => type switch
    {
        "Temperature" => Ui.UnitSymbol,
        "Load" or "Control" or "Level" or "Humidity" => "%",
        "Fan" => "RPM",
        "Clock" => "MHz",
        "Power" => "W",
        "Voltage" => "V",
        "Current" => "A",
        "Frequency" => "Hz",
        "Data" => "GB",
        "SmallData" => "MB",
        "Throughput" => "B/s",
        "Energy" => "mWh",
        "Noise" => "dBA",
        "Flow" => "L/h",
        _ => "",
    };

    public static string Format(string type, float value) =>
        type == "Temperature" ? Ui.Temp(value) : $"{value:0.##} {UnitFor(type)}".TrimEnd();
}

public sealed record DeviceSnapshot(
    string Name,
    float? Temperature,
    string? TemperatureSource,
    float? LoadPercent,
    float? ClockMhz,
    float? PowerWatts,
    float? MemoryUsedMb,
    float? MemoryTotalMb,
    float? FanRpm,
    float? FanPercent,
    IReadOnlyList<SensorValue> Temperatures,
    IReadOnlyList<SensorReading> Sensors);

public sealed record HardwareSnapshot(DeviceSnapshot? Cpu, DeviceSnapshot? Gpu);

/// <summary>
/// Not thread-safe: call <see cref="Read"/> from one thread at a time.
/// </summary>
public sealed class HardwareReader : IDisposable
{
    private readonly Computer _computer;
    private readonly UpdateVisitor _visitor = new();

    public HardwareReader()
    {
        _computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true };
        _computer.Open();
    }

    public HardwareSnapshot Read()
    {
        _computer.Accept(_visitor);

        DeviceSnapshot? cpu = _computer.Hardware
            .Where(h => h.HardwareType == HardwareType.Cpu)
            .Select(h => Snapshot(h, isGpu: false))
            .MaxBy(d => d.Temperature ?? float.MinValue);

        // Prefer a discrete GPU over integrated graphics when both are present.
        DeviceSnapshot? gpu = _computer.Hardware
            .Where(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
            .OrderBy(h => h.HardwareType == HardwareType.GpuIntel ? 1 : 0)
            .ThenByDescending(h => Hottest(Temperatures(AllSensors(h).ToList()))?.Value ?? float.MinValue)
            .Select(h => Snapshot(h, isGpu: true))
            .FirstOrDefault();

        return new HardwareSnapshot(cpu, gpu);
    }

    private static DeviceSnapshot Snapshot(IHardware hardware, bool isGpu)
    {
        List<ISensor> sensors = AllSensors(hardware).Where(s => s.Value is not null).ToList();
        List<SensorValue> temperatures = Temperatures(sensors);
        SensorValue? hottest = Hottest(temperatures);

        float? clock = isGpu
            ? Named(sensors, SensorType.Clock, "GPU Core")
            // Intel names these "CPU Core #1", AMD "Core #1" and hybrid Intel "P-Core #1" / "E-Core #1".
            : Average(sensors.Where(s => s.SensorType == SensorType.Clock
                                         && s.Name.Contains("Core #", StringComparison.OrdinalIgnoreCase)));

        // Sensors that need the PawnIO driver report 0 when it's missing.
        List<ISensor> powerSensors = sensors.Where(s => s.SensorType == SensorType.Power && s.Value > 0).ToList();
        float? power =
            powerSensors.FirstOrDefault(s => s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase))?.Value
            ?? powerSensors.FirstOrDefault()?.Value;

        return new DeviceSnapshot(
            Name: hardware.Name,
            Temperature: hottest?.Value,
            TemperatureSource: hottest?.Name,
            LoadPercent: Named(sensors, SensorType.Load, isGpu ? "GPU Core" : "CPU Total")
                         ?? sensors.FirstOrDefault(s => s.SensorType == SensorType.Load)?.Value,
            ClockMhz: clock,
            PowerWatts: power,
            MemoryUsedMb: isGpu ? Named(sensors, SensorType.SmallData, "GPU Memory Used", "D3D Dedicated Memory Used") : null,
            MemoryTotalMb: isGpu ? Named(sensors, SensorType.SmallData, "GPU Memory Total") : null,
            FanRpm: isGpu ? sensors.FirstOrDefault(s => s.SensorType == SensorType.Fan)?.Value : null,
            FanPercent: isGpu
                ? sensors.FirstOrDefault(s => s.SensorType == SensorType.Control && s.Name.Contains("Fan", StringComparison.OrdinalIgnoreCase))?.Value
                : null,
            Temperatures: temperatures,
            Sensors: Readings(sensors));
    }

    private static List<SensorReading> Readings(List<ISensor> sensors) => sensors
        // "Distance to TjMax" is headroom, not a temperature.
        .Where(s => !s.Name.Contains("Distance", StringComparison.OrdinalIgnoreCase))
        .Where(s => s.SensorType != SensorType.Temperature || s.Value > 0)
        .Where(s => !float.IsNaN(s.Value!.Value))
        .Select(s => new SensorReading(s.Name, s.SensorType.ToString(), s.Value!.Value))
        .OrderBy(r => r.Type, StringComparer.OrdinalIgnoreCase)
        .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static List<SensorValue> Temperatures(List<ISensor> sensors) => sensors
        .Where(s => s.SensorType == SensorType.Temperature && s.Value is > 0)
        // "Distance to TjMax" sensors report headroom, not temperature.
        .Where(s => !s.Name.Contains("Distance", StringComparison.OrdinalIgnoreCase))
        .Select(s => new SensorValue(s.Name, s.Value!.Value))
        .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static SensorValue? Hottest(List<SensorValue> temperatures) =>
        temperatures.Count == 0 ? null : temperatures.MaxBy(t => t.Value);

    private static float? Named(List<ISensor> sensors, SensorType type, params string[] names)
    {
        foreach (string name in names)
        {
            ISensor? match = sensors.FirstOrDefault(s => s.SensorType == type && s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match.Value;
        }

        return null;
    }

    private static float? Average(IEnumerable<ISensor> sensors)
    {
        float[] values = sensors.Select(s => s.Value!.Value).Where(v => v > 0).ToArray();
        return values.Length == 0 ? null : values.Average();
    }

    private static IEnumerable<ISensor> AllSensors(IHardware hardware)
    {
        foreach (ISensor sensor in hardware.Sensors)
            yield return sensor;
        foreach (IHardware sub in hardware.SubHardware)
        foreach (ISensor sensor in AllSensors(sub))
            yield return sensor;
    }

    public void Dispose() => _computer.Close();

    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (IHardware sub in hardware.SubHardware)
                sub.Accept(this);
        }

        public void VisitSensor(ISensor sensor) { }

        public void VisitParameter(IParameter parameter) { }
    }
}
