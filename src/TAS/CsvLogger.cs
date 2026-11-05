using System.Globalization;
using System.Text;

namespace TAS;

/// <summary>
/// Appends one row per sensor check to readings.csv. Temperatures are always in °C.
/// </summary>
internal static class CsvLogger
{
    private const long MaxBytes = 10 * 1024 * 1024;
    private const string Header = "Time,CpuTempC,CpuLoadPct,CpuPowerW,CpuClockMHz,GpuTempC,GpuLoadPct,GpuPowerW,GpuClockMHz,GpuFanRpm";

    public static string FilePath { get; } = Path.Combine(Path.GetDirectoryName(Settings.FilePath)!, "readings.csv");

    public static void Append(HardwareSnapshot s)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

            // Keep one previous file so the log can't grow without limit.
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes)
                File.Move(FilePath, FilePath + ".old", overwrite: true);

            var row = new StringBuilder();
            if (!File.Exists(FilePath))
                row.AppendLine(Header);

            row.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            Add(row, s.Cpu?.Temperature);
            Add(row, s.Cpu?.LoadPercent);
            Add(row, s.Cpu?.PowerWatts);
            Add(row, s.Cpu?.ClockMhz);
            Add(row, s.Gpu?.Temperature);
            Add(row, s.Gpu?.LoadPercent);
            Add(row, s.Gpu?.PowerWatts);
            Add(row, s.Gpu?.ClockMhz);
            Add(row, s.Gpu?.FanRpm);
            row.AppendLine();

            File.AppendAllText(FilePath, row.ToString(), new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Another program may have the file open; skip this row.
        }
    }

    private static void Add(StringBuilder row, float? value)
    {
        row.Append(',');
        if (value is float v)
            row.Append(v.ToString("0.#", CultureInfo.InvariantCulture));
    }
}
