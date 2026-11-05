namespace TAS;

/// <summary>
/// Lists the user's custom alerts and lets them add, edit, switch off or remove them.
/// </summary>
internal sealed class RulesForm : Form
{
    private readonly Settings _settings;
    private readonly HardwareSnapshot? _snapshot;
    private readonly ListView _list = new()
    {
        View = View.Details,
        FullRowSelect = true,
        CheckBoxes = true,
        HideSelection = false,
        MultiSelect = false,
        HeaderStyle = ColumnHeaderStyle.None,
        Size = new Size(Ui.Scale(520), Ui.Scale(220)),
    };
    private readonly Button _edit = new() { Text = "Edit...", AutoSize = true, Enabled = false };
    private readonly Button _remove = new() { Text = "Remove", AutoSize = true, Enabled = false };
    private bool _loading;

    public RulesForm(Settings settings, HardwareSnapshot? snapshot)
    {
        _settings = settings;
        _snapshot = snapshot;

        Text = "TAS - Custom alerts";
        Font = new Font("Segoe UI", 9f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        _list.Columns.Add("Alert", Ui.Scale(490));

        var intro = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(Ui.Scale(520), 0),
            Margin = new Padding(0, 0, 0, Ui.Scale(6)),
            Text = "Get an alert for any other reading, for example GPU hot spot or memory temperature, CPU or GPU load, or a fan that stops. "
                   + "These work alongside the CPU and GPU temperature limits in the main window.",
        };

        var add = new Button { Text = "Add...", AutoSize = true };
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK };
        CancelButton = close;

        add.Click += (_, _) => AddRule();
        _edit.Click += (_, _) => EditSelected();
        _remove.Click += (_, _) => RemoveSelected();
        _list.SelectedIndexChanged += (_, _) => _edit.Enabled = _remove.Enabled = _list.SelectedItems.Count > 0;
        _list.DoubleClick += (_, _) => EditSelected();
        _list.ItemChecked += (_, e) =>
        {
            if (_loading || e.Item.Tag is not AlertRule rule)
                return;
            rule.Enabled = e.Item.Checked;
            _settings.Save();
        };
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete)
                RemoveSelected();
        };

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, Ui.Scale(6), 0, 0) };
        buttons.Controls.Add(add);
        buttons.Controls.Add(_edit);
        buttons.Controls.Add(_remove);

        var closeRow = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(0, Ui.Scale(8), 0, 0) };
        closeRow.Controls.Add(close);

        var root = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(Ui.Scale(12)) };
        root.Controls.Add(intro);
        root.Controls.Add(_list);
        root.Controls.Add(buttons);
        root.Controls.Add(closeRow);
        Controls.Add(root);

        Reload();
    }

    private void Reload()
    {
        _loading = true;
        _list.Items.Clear();
        foreach (AlertRule rule in _settings.CustomAlerts)
            _list.Items.Add(new ListViewItem(rule.Describe()) { Checked = rule.Enabled, Tag = rule });
        _loading = false;
    }

    private void AddRule()
    {
        var rule = new AlertRule();
        using var editor = new RuleEditorForm(rule, _snapshot);
        if (editor.ShowDialog(this) != DialogResult.OK)
            return;

        _settings.CustomAlerts.Add(rule);
        _settings.Save();
        Reload();
    }

    private void EditSelected()
    {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not AlertRule rule)
            return;

        using var editor = new RuleEditorForm(rule, _snapshot);
        if (editor.ShowDialog(this) != DialogResult.OK)
            return;

        _settings.Save();
        Reload();
    }

    private void RemoveSelected()
    {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not AlertRule rule)
            return;

        _settings.CustomAlerts.Remove(rule);
        _settings.Save();
        Reload();
    }
}
