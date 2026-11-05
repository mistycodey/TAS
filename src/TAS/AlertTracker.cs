namespace TAS;

/// <summary>
/// Decides when a reading should raise a toast, so a temperature hovering at the threshold doesn't spam alerts.
/// </summary>
public sealed class AlertTracker
{
    private bool _alerting;
    private DateTime _lastAlertUtc;

    /// <summary>
    /// Forgets any previous alert so the next reading above the threshold alerts immediately.
    /// </summary>
    public void Reset()
    {
        _alerting = false;
        _lastAlertUtc = default;
    }

    public bool ShouldAlert(float? value, float threshold, float hysteresis, int repeatMinutes)
    {
        if (value is not float temp)
            return false;

        if (_alerting && temp < threshold - hysteresis)
            _alerting = false;

        if (temp < threshold)
            return false;

        DateTime now = DateTime.UtcNow;
        bool repeatDue = repeatMinutes > 0 && now - _lastAlertUtc >= TimeSpan.FromMinutes(repeatMinutes);
        if (_alerting && !repeatDue)
            return false;

        _alerting = true;
        _lastAlertUtc = now;
        return true;
    }
}
