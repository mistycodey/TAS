using System.Text.Json;
using System.Text.Json.Serialization;

namespace TAS;

public enum AlertSoundKind { NotificationOnly, Warning, Critical, CustomFile }

public sealed class Settings
{
    public float CpuThresholdC { get; set; } = 85;
    public float GpuThresholdC { get; set; } = 80;
    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// After an alert, the temperature must drop this far below the threshold before another alert can fire.
    /// </summary>
    public float HysteresisC { get; set; } = 3;

    /// <summary>
    /// While still above the threshold, repeat the alert after this many minutes. 0 disables repeats.
    /// </summary>
    public int RepeatAlertMinutes { get; set; } = 10;

    /// <summary>
    /// The help guide opens automatically on first launch.
    /// </summary>
    public bool HasSeenHelp { get; set; }

    /// <summary>
    /// Extra sound TAS plays itself on alerts, on top of the Windows notification sound.
    /// </summary>
    public AlertSoundKind AlertSound { get; set; } = AlertSoundKind.Warning;

    public string? CustomSoundPath { get; set; }

    /// <summary>
    /// Also show alerts as an always-on-top popup, because Windows hides its own notifications over games and full-screen apps.
    /// </summary>
    public bool ShowOverlayAlerts { get; set; } = true;

    /// <summary>
    /// Longest side, in pixels at 100% display scaling, of the picture in an alert pop-up.
    /// </summary>
    public int AlertImageSize { get; set; } = 720;

    /// <summary>
    /// Optional images (http/https addresses) for CPU temperature alerts. Each alert shows one of them, picked at random.
    /// </summary>
    public List<string> CpuImageUrls { get; set; } = new();

    /// <summary>
    /// Optional images (http/https addresses) for GPU temperature alerts. Each alert shows one of them, picked at random.
    /// </summary>
    public List<string> GpuImageUrls { get; set; } = new();

    /// <summary>
    /// User-defined alerts on other readings (hot spot, load, fan speed, ...), in addition to the CPU/GPU temperature limits.
    /// </summary>
    public List<AlertRule> CustomAlerts { get; set; } = new();

    /// <summary>
    /// Display unit only. Limits and logs are always stored in °C.
    /// </summary>
    public bool UseFahrenheit { get; set; }

    /// <summary>
    /// Append every reading to readings.csv next to the settings file.
    /// </summary>
    public bool LogReadingsToCsv { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TAS", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), JsonOptions) ?? new Settings();
        }
        catch (Exception)
        {
            // Corrupt file: fall back to defaults and overwrite on next save.
        }

        var defaults = new Settings();
        defaults.Save();
        return defaults;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }
}
