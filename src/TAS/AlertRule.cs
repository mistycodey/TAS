namespace TAS;

public enum AlertDevice { Cpu, Gpu }

public enum AlertComparison { Above, Below }

/// <summary>
/// A user-defined alert on a single sensor reading (hot spot, load, fan speed, ...).
/// </summary>
public sealed class AlertRule
{
    public bool Enabled { get; set; } = true;

    public AlertDevice Device { get; set; } = AlertDevice.Gpu;

    /// <summary>
    /// Sensor name as reported by the hardware library, e.g. "GPU Hot Spot".
    /// </summary>
    public string SensorName { get; set; } = "";

    /// <summary>
    /// Sensor type as reported by the hardware library, e.g. "Temperature", "Load", "Fan".
    /// </summary>
    public string SensorType { get; set; } = "Temperature";

    public AlertComparison Comparison { get; set; } = AlertComparison.Above;

    /// <summary>
    /// Limit in the sensor's own unit (°C for temperatures, %, RPM, ...). Reaching it triggers the alert.
    /// </summary>
    public float Limit { get; set; }

    /// <summary>
    /// After an alert, the reading must recover this far past the limit (in the sensor's own unit) before it can alert again.
    /// </summary>
    public float Hysteresis { get; set; } = 3;

    /// <summary>
    /// Optional images (http/https addresses) for the TAS pop-up when this alert fires. One is picked at random each time.
    /// </summary>
    public List<string> ImageUrls { get; set; } = new();

    public SensorReading? Find(DeviceSnapshot? device)
    {
        if (device is null)
            return null;

        foreach (SensorReading sensor in device.Sensors)
        {
            if (sensor.Type == SensorType && sensor.Name.Equals(SensorName, StringComparison.OrdinalIgnoreCase))
                return sensor;
        }

        return null;
    }

    public string Describe() =>
        $"{DeviceName}: {SensorName} {(Comparison == AlertComparison.Above ? "above" : "below")} {SensorInfo.Format(SensorType, Limit)}";

    public string DeviceName => Device == AlertDevice.Cpu ? "CPU" : "GPU";
}
