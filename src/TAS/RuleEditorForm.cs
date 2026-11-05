namespace TAS;

/// <summary>
/// Dialog for creating or editing one custom alert. Changes are only written to the rule when OK is clicked.
/// </summary>
internal sealed class RuleEditorForm : Form
{
    private sealed record SensorChoice(string Name, string Type)
    {
        public override string ToString() => $"{Name}  ({Type})";
    }

    private readonly AlertRule _rule;
    private readonly HardwareSnapshot? _snapshot;
    private readonly ComboBox _device = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = Ui.Scale(280) };
    private readonly ComboBox _sensor = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = Ui.Scale(280) };
    private readonly ComboBox _comparison = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = Ui.Scale(280) };
    private readonly NumericUpDown _limit = new() { DecimalPlaces = 1, Minimum = -1_000_000, Maximum = 1_000_000, Width = Ui.Scale(100), TextAlign = HorizontalAlignment.Right };
    private readonly NumericUpDown _hysteresis = new() { DecimalPlaces = 1, Minimum = 0, Maximum = 1_000_000, Width = Ui.Scale(100), TextAlign = HorizontalAlignment.Right };
    private readonly Label _limitUnit = Ui.Caption("");
    private readonly Label _hysteresisUnit = Ui.Caption("");

    public RuleEditorForm(AlertRule rule, HardwareSnapshot? snapshot)
    {
        _rule = rule;
        _snapshot = snapshot;

        Text = "Custom alert";
        Font = new Font("Segoe UI", 9f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        _device.Items.AddRange(["CPU", "GPU"]);
        _comparison.Items.AddRange(["Goes above (at or over the limit)", "Drops below (at or under the limit)"]);

        _device.SelectedIndex = (int)rule.Device;
        _comparison.SelectedIndex = (int)rule.Comparison;
        FillSensors(rule.SensorName, rule.SensorType);
        LoadValues();

        _device.SelectedIndexChanged += (_, _) => FillSensors(null, null);
        _sensor.SelectedIndexChanged += (_, _) => UpdateUnits();

        var table = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(Ui.Scale(12)) };
        AddRow(table, "Device", _device, null);
        AddRow(table, "Sensor", _sensor, null);
        AddRow(table, "Alert when it", _comparison, null);
        AddRow(table, "Limit", _limit, _limitUnit);
        AddRow(table, "Alert again after recovering by", _hysteresis, _hysteresisUnit);

        var note = new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            MaximumSize = new Size(Ui.Scale(480), 0),
            Margin = new Padding(Ui.Scale(3), Ui.Scale(4), 0, Ui.Scale(8)),
            Text = _snapshot is null
                ? "TAS hasn't read the sensors yet. Close this, wait a few seconds and try again to see the full sensor list."
                : "Only sensors your hardware currently reports are listed. The alert uses the same \"repeat\" and cooldown settings as the CPU and GPU limits.",
        };
        table.Controls.Add(note);
        table.SetColumnSpan(note, 3);

        var ok = new Button { Text = "OK", AutoSize = true, DialogResult = DialogResult.None };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) => Save();
        AcceptButton = ok;
        CancelButton = cancel;
        var buttons = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Right, Margin = Padding.Empty };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        table.Controls.Add(buttons);
        table.SetColumnSpan(buttons, 3);

        Controls.Add(table);
    }

    private static void AddRow(TableLayoutPanel table, string caption, Control input, Label? unit)
    {
        table.Controls.Add(Ui.Caption(caption));
        table.Controls.Add(input);
        table.Controls.Add(unit ?? new Label { AutoSize = true });
    }

    private bool IsTemperature => _sensor.SelectedItem is SensorChoice { Type: "Temperature" };

    private void FillSensors(string? keepName, string? keepType)
    {
        DeviceSnapshot? device = _device.SelectedIndex == (int)AlertDevice.Cpu ? _snapshot?.Cpu : _snapshot?.Gpu;
        var choices = (device?.Sensors ?? []).Select(s => new SensorChoice(s.Name, s.Type)).ToList();

        // Keep a rule's sensor selectable even when the hardware isn't reporting it right now.
        if (keepName is { Length: > 0 } && keepType is not null && !choices.Any(c => c.Name == keepName && c.Type == keepType))
            choices.Insert(0, new SensorChoice(keepName, keepType));

        _sensor.Items.Clear();
        _sensor.Items.AddRange(choices.Cast<object>().ToArray());

        int index = keepName is null ? 0 : choices.FindIndex(c => c.Name == keepName && c.Type == keepType);
        if (_sensor.Items.Count > 0)
            _sensor.SelectedIndex = Math.Max(0, index);
        UpdateUnits();
    }

    private void LoadValues()
    {
        UpdateUnits();
        _limit.Value = Clamp(_limit, IsTemperature ? Ui.ToDisplay(_rule.Limit) : _rule.Limit);
        _hysteresis.Value = Clamp(_hysteresis, IsTemperature ? Ui.DeltaToDisplay(_rule.Hysteresis) : _rule.Hysteresis);
    }

    private static decimal Clamp(NumericUpDown input, float value) =>
        Math.Clamp((decimal)Math.Round(value, 1), input.Minimum, input.Maximum);

    private void UpdateUnits()
    {
        string type = (_sensor.SelectedItem as SensorChoice)?.Type ?? "Temperature";
        string unit = SensorInfo.UnitFor(type);
        _limitUnit.Text = unit;
        _hysteresisUnit.Text = unit;
    }

    private void Save()
    {
        if (_sensor.SelectedItem is not SensorChoice sensor)
        {
            MessageBox.Show(this, "Pick a sensor first. If the list is empty, TAS hasn't read that device's sensors yet.", "TAS",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        bool temperature = sensor.Type == "Temperature";
        _rule.Device = (AlertDevice)_device.SelectedIndex;
        _rule.SensorName = sensor.Name;
        _rule.SensorType = sensor.Type;
        _rule.Comparison = (AlertComparison)_comparison.SelectedIndex;
        _rule.Limit = temperature ? Ui.FromDisplay((float)_limit.Value) : (float)_limit.Value;
        _rule.Hysteresis = temperature ? Ui.DeltaFromDisplay((float)_hysteresis.Value) : (float)_hysteresis.Value;
        DialogResult = DialogResult.OK;
    }
}
