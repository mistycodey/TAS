namespace TAS;

/// <summary>
/// Temperature history graph with a time-range picker and the peak temperatures since TAS started.
/// </summary>
internal sealed class HistoryPanel : UserControl
{
    private static readonly (string Label, TimeSpan Window)[] Ranges =
    [
        ("Last 10 minutes", TimeSpan.FromMinutes(10)),
        ("Last 30 minutes", TimeSpan.FromMinutes(30)),
        ("Last hour", TimeSpan.FromHours(1)),
    ];

    private readonly Settings _settings;
    private readonly HistoryStore _store;
    private readonly HistoryChart _chart = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _range = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = Ui.Scale(150) };
    private readonly Label _peaks = new() { AutoSize = true, MaximumSize = new Size(Ui.Scale(640), 0) };
    private readonly Label _note = new()
    {
        AutoSize = true,
        ForeColor = SystemColors.GrayText,
        Text = "The graph keeps the last hour and starts again when TAS restarts.",
        Margin = new Padding(Ui.Scale(3), Ui.Scale(2), 0, 0),
    };

    public HistoryPanel(Settings settings, HistoryStore store)
    {
        _settings = settings;
        _store = store;
        Dock = DockStyle.Fill;

        foreach ((string label, _) in Ranges)
            _range.Items.Add(label);
        _range.SelectedIndex = 0;
        _range.SelectedIndexChanged += (_, _) => UpdateData();

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        top.Controls.Add(Ui.Caption("Show"));
        top.Controls.Add(_range);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        bottom.Controls.Add(_peaks);
        bottom.Controls.Add(_note);

        Controls.Add(_chart);
        Controls.Add(top);
        Controls.Add(bottom);
    }

    public void UpdateData()
    {
        TimeSpan window = Ranges[Math.Max(0, _range.SelectedIndex)].Window;
        _chart.SetData(_store.Since(window), window, _settings.CpuThresholdC, _settings.GpuThresholdC);

        _peaks.Text = $"Peak since TAS started ({_store.Started:g}):   CPU {Describe(_store.PeakCpu)}   |   GPU {Describe(_store.PeakGpu)}";
    }

    private static string Describe(HistoryStore.Peak? peak) =>
        peak is { } p ? $"{Ui.Temp(p.Value)} at {p.Time:T}" : "n/a";
}
