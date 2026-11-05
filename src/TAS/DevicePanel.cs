namespace TAS;

/// <summary>
/// Live readout for one device (CPU or GPU) plus its alert threshold.
/// </summary>
internal sealed class DevicePanel : GroupBox
{
    private readonly bool _isGpu;
    private readonly Label _model = new() { AutoSize = true, Text = "Reading sensors..." };
    private readonly Label _temperature = new() { AutoSize = true, Text = "--", Margin = new Padding(0, 4, 0, 0) };
    private readonly Label _temperatureSource = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly Label _load = Value();
    private readonly Label _clock = Value();
    private readonly Label _power = Value();
    private readonly Label _memory = Value();
    private readonly Label _fan = Value();
    private readonly NumericUpDown _threshold = Ui.Number(30, 110);
    private readonly Label _unit = Ui.Caption("°C");
    private bool _suppress;
    private readonly ListView _sensors = new()
    {
        View = View.Details,
        FullRowSelect = true,
        HeaderStyle = ColumnHeaderStyle.Nonclickable,
        Size = new Size(Ui.Scale(330), Ui.Scale(200)),
        Margin = new Padding(0, Ui.Scale(6), 0, 0),
    };

    public event Action<float>? ThresholdChanged;

    /// <summary>
    /// The alert limit in °C. The box shows it in the current display unit (°C or °F).
    /// </summary>
    public float Threshold
    {
        get => Ui.FromDisplay((float)_threshold.Value);
        set
        {
            _suppress = true;
            try
            {
                _threshold.Minimum = (decimal)Math.Round(Ui.ToDisplay(30));
                _threshold.Maximum = (decimal)Math.Round(Ui.ToDisplay(110));
                Ui.SetClamped(_threshold, (decimal)Math.Round(Ui.ToDisplay(value)));
                _unit.Text = Ui.UnitSymbol;
            }
            finally
            {
                _suppress = false;
            }
        }
    }

    public DevicePanel(string title, bool isGpu)
    {
        _isGpu = isGpu;
        Text = title;
        Dock = DockStyle.Fill;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(Ui.Scale(10));

        _model.Font = new Font(Font, FontStyle.Bold);
        _temperature.Font = new Font(Font.FontFamily, 28f, FontStyle.Bold);
        _sensors.Columns.Add("Temperature sensor", Ui.Scale(230));
        _sensors.Columns.Add("Value", Ui.Scale(75), HorizontalAlignment.Right);
        _threshold.ValueChanged += (_, _) =>
        {
            if (!_suppress)
                ThresholdChanged?.Invoke(Threshold);
        };

        var stats = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        AddStat(stats, "Load", _load);
        AddStat(stats, "Clock", _clock);
        AddStat(stats, "Power", _power);
        if (isGpu)
        {
            AddStat(stats, "Memory", _memory);
            AddStat(stats, "Fan", _fan);
        }

        var alertRow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0, Ui.Scale(6), 0, Ui.Scale(6)),
        };
        Label alertCaption = Ui.Caption("Alert when above");
        alertCaption.Font = new Font(Font, FontStyle.Bold);
        alertRow.Controls.Add(alertCaption);
        alertRow.Controls.Add(_threshold);
        alertRow.Controls.Add(_unit);

        // Every row sizes to its content so nothing is clipped at any display scaling.
        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
        };
        root.Controls.Add(_model);
        root.Controls.Add(_temperature);
        root.Controls.Add(_temperatureSource);
        root.Controls.Add(alertRow);
        root.Controls.Add(stats);
        root.Controls.Add(_sensors);
        Controls.Add(root);
    }

    public void ShowDevice(DeviceSnapshot? device)
    {
        float threshold = Threshold;

        if (device is null)
        {
            _model.Text = _isGpu ? "No GPU detected" : "No CPU detected";
            _temperature.Text = "--";
            _temperature.ForeColor = SystemColors.GrayText;
            _temperatureSource.Text = "";
            foreach (Label label in new[] { _load, _clock, _power, _memory, _fan })
                label.Text = "n/a";
            _sensors.Items.Clear();
            return;
        }

        _model.Text = device.Name;
        _temperature.Text = Ui.Temp(device.Temperature);
        _temperature.ForeColor = Ui.TemperatureColor(device.Temperature, threshold);
        _temperatureSource.Text = device.TemperatureSource is null
            ? "No temperature sensor available"
            : $"Hottest sensor: {device.TemperatureSource}";

        _load.Text = Format(device.LoadPercent, "0", "%");
        _clock.Text = Format(device.ClockMhz, "#,0", " MHz");
        _power.Text = Format(device.PowerWatts, "0.0", " W");
        _memory.Text = device switch
        {
            { MemoryUsedMb: float used, MemoryTotalMb: float total } => $"{used:#,0} / {total:#,0} MB",
            { MemoryUsedMb: float used } => $"{used:#,0} MB used",
            _ => "n/a",
        };
        _fan.Text = (device.FanRpm, device.FanPercent) switch
        {
            (float rpm, float pct) => $"{rpm:#,0} RPM ({pct:0}%)",
            (float rpm, null) => $"{rpm:#,0} RPM",
            (null, float pct) => $"{pct:0}%",
            _ => "n/a",
        };

        UpdateSensorList(device.Temperatures, threshold);
    }

    private void UpdateSensorList(IReadOnlyList<SensorValue> temperatures, float threshold)
    {
        bool sameSensors = _sensors.Items.Count == temperatures.Count
            && temperatures.Select((t, i) => _sensors.Items[i].Text == t.Name).All(x => x);

        _sensors.BeginUpdate();
        if (!sameSensors)
        {
            _sensors.Items.Clear();
            foreach (SensorValue t in temperatures)
                _sensors.Items.Add(new ListViewItem(new[] { t.Name, "" }) { UseItemStyleForSubItems = true });
        }

        for (int i = 0; i < temperatures.Count; i++)
        {
            ListViewItem item = _sensors.Items[i];
            item.SubItems[1].Text = Ui.Temp(temperatures[i].Value);
            item.ForeColor = temperatures[i].Value >= threshold ? Color.Firebrick : SystemColors.WindowText;
        }
        _sensors.EndUpdate();
    }

    private static string Format(float? value, string format, string unit) =>
        value is float v ? v.ToString(format) + unit : "n/a";

    private static Label Value() => new() { AutoSize = true, Text = "--", Anchor = AnchorStyles.Left, Margin = new Padding(0, Ui.Scale(3), 0, Ui.Scale(3)) };

    private static void AddStat(TableLayoutPanel stats, string caption, Label value)
    {
        Label label = Ui.Caption(caption);
        label.ForeColor = SystemColors.GrayText;
        label.Margin = new Padding(0, Ui.Scale(3), Ui.Scale(16), Ui.Scale(3));
        stats.Controls.Add(label);
        stats.Controls.Add(value);
    }
}
