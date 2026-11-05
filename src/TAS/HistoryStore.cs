namespace TAS;

/// <summary>
/// Remembers the last hour of CPU/GPU temperatures for the history graph, plus the peaks since TAS started.
/// Only used from the UI thread.
/// </summary>
internal sealed class HistoryStore
{
    public readonly record struct Sample(DateTime Time, float? Cpu, float? Gpu);

    public readonly record struct Peak(float Value, DateTime Time);

    public static readonly TimeSpan Retention = TimeSpan.FromHours(1);

    private readonly List<Sample> _samples = new();

    public DateTime Started { get; } = DateTime.Now;

    public Peak? PeakCpu { get; private set; }

    public Peak? PeakGpu { get; private set; }

    public void Add(HardwareSnapshot s)
    {
        DateTime now = DateTime.Now;
        float? cpu = s.Cpu?.Temperature;
        float? gpu = s.Gpu?.Temperature;
        _samples.Add(new Sample(now, cpu, gpu));

        if (cpu is float c && (PeakCpu is null || c > PeakCpu.Value.Value))
            PeakCpu = new Peak(c, now);
        if (gpu is float g && (PeakGpu is null || g > PeakGpu.Value.Value))
            PeakGpu = new Peak(g, now);

        DateTime cutoff = now - Retention;
        int expired = 0;
        while (expired < _samples.Count && _samples[expired].Time < cutoff)
            expired++;
        if (expired > 0)
            _samples.RemoveRange(0, expired);
    }

    public Sample[] Since(TimeSpan window)
    {
        DateTime cutoff = DateTime.Now - window;
        return _samples.Where(s => s.Time >= cutoff).ToArray();
    }
}
