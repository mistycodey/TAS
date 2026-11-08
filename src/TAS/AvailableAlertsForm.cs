namespace TAS;

/// <summary>
/// Lists every sensor the hardware currently reports, with a one-line explanation of what each one monitors,
/// so a custom alert can be created from any of them.
/// </summary>
internal sealed class AvailableAlertsForm : Form
{
    private readonly Settings _settings;
    private readonly HardwareSnapshot? _snapshot;
    private readonly ListView _sensors = new()
    {
        View = View.Details,
        FullRowSelect = true,
        HideSelection = false,
        MultiSelect = false,
        ShowGroups = true,
        ShowItemToolTips = true,
        HeaderStyle = ColumnHeaderStyle.Nonclickable,
        Dock = DockStyle.Fill,
    };
    private readonly Button _create = new() { Text = "Create alert for selected...", AutoSize = true, Enabled = false };

    /// <summary>
    /// True if at least one alert was created from this window.
    /// </summary>
    public bool AlertsChanged { get; private set; }

    public AvailableAlertsForm(Settings settings, HardwareSnapshot? snapshot)
    {
        _settings = settings;
        _snapshot = snapshot;

        Text = "TAS - Available alerts";
        Font = new Font("Segoe UI", 9f);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        ClientSize = new Size(Ui.Scale(820), Ui.Scale(560));
        MinimumSize = Size;

        _sensors.Columns.Add("Sensor", Ui.Scale(210));
        _sensors.Columns.Add("Now", Ui.Scale(80));
        _sensors.Columns.Add("What it monitors", Ui.Scale(480));
        _sensors.SizeChanged += (_, _) => _sensors.Columns[2].Width =
            Math.Max(Ui.Scale(150), _sensors.ClientSize.Width - _sensors.Columns[0].Width - _sensors.Columns[1].Width);

        string noteText = _snapshot is null
            ? "TAS hasn't read the sensors yet. Close this, wait a few seconds and try again."
            : "Every sensor your CPU and GPU report right now. Not every model reports every sensor. "
              + "Double-click one to create an alert for it.";
        var note = new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            MaximumSize = new Size(Ui.Scale(780), 0),
            Margin = new Padding(0, 0, 0, Ui.Scale(8)),
            Text = noteText,
        };

        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK };
        CancelButton = close;
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, Ui.Scale(8), 0, 0) };
        buttons.Controls.Add(_create);
        buttons.Controls.Add(close);

        var root = new TableLayoutPanel { ColumnCount = 1, RowCount = 3, Dock = DockStyle.Fill, Padding = new Padding(Ui.Scale(12)) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(note, 0, 0);
        root.Controls.Add(_sensors, 0, 1);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);

        _sensors.SelectedIndexChanged += (_, _) => _create.Enabled = _sensors.SelectedItems.Count > 0;
        _sensors.DoubleClick += (_, _) => CreateFromSelected();
        _create.Click += (_, _) => CreateFromSelected();

        AddSensors("CPU", AlertDevice.Cpu, _snapshot?.Cpu);
        AddSensors("GPU", AlertDevice.Gpu, _snapshot?.Gpu);
    }

    private void AddSensors(string deviceName, AlertDevice device, DeviceSnapshot? snapshot)
    {
        if (snapshot is null)
            return;

        var groups = new Dictionary<string, ListViewGroup>();
        foreach (SensorReading sensor in snapshot.Sensors)
        {
            if (!groups.TryGetValue(sensor.Type, out ListViewGroup? group))
            {
                group = new ListViewGroup($"{deviceName} - {SensorDescriptions.TypeLabel(sensor.Type)}");
                groups[sensor.Type] = group;
                _sensors.Groups.Add(group);
            }

            string description = SensorDescriptions.Describe(sensor.Name, sensor.Type, device == AlertDevice.Gpu);
            var item = new ListViewItem(sensor.Name, group) { Tag = (device, sensor) };
            item.SubItems.Add(SensorInfo.Format(sensor.Type, sensor.Value));
            item.SubItems.Add(description);
            item.ToolTipText = description;
            _sensors.Items.Add(item);
        }
    }

    private void CreateFromSelected()
    {
        if (_sensors.SelectedItems.Count == 0 || _sensors.SelectedItems[0].Tag is not ValueTuple<AlertDevice, SensorReading> pick)
            return;

        (AlertDevice device, SensorReading sensor) = pick;
        var rule = new AlertRule
        {
            Device = device,
            SensorName = sensor.Name,
            SensorType = sensor.Type,
        };
        SuggestLimit(rule, sensor);

        using var editor = new RuleEditorForm(rule, _snapshot);
        if (editor.ShowDialog(this) != DialogResult.OK)
            return;

        _settings.CustomAlerts.Add(rule);
        _settings.Save();
        AlertsChanged = true;
        MessageBox.Show(this, $"Alert added: {rule.Describe()}.\n\nYou can switch it off or remove it under \"Manage custom alerts\".",
            "TAS", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // A starting point only; the editor lets the user change it.
    private static void SuggestLimit(AlertRule rule, SensorReading sensor)
    {
        switch (sensor.Type)
        {
            case "Temperature":
                rule.Limit = (float)Math.Ceiling(sensor.Value) + 10;
                break;
            case "Load" or "Control" or "Level":
                rule.Limit = 90;
                break;
            case "Fan":
                rule.Comparison = AlertComparison.Below;
                rule.Limit = (float)Math.Round(sensor.Value / 2 / 50) * 50;
                break;
            default:
                rule.Limit = (float)Math.Ceiling(sensor.Value * 1.2);
                break;
        }
    }
}
