using System.Diagnostics;

namespace TAS;

public sealed class MainForm : Form
{
    private readonly Settings _settings;
    private readonly SetupPanel _setup = new();
    private readonly DevicePanel _cpuPanel = new("CPU", isGpu: false);
    private readonly DevicePanel _gpuPanel = new("GPU", isGpu: true);
    private readonly HistoryPanel _historyPanel;
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly TabPage _historyPage = NewPage("History");
    private readonly TabPage _setupPage = NewPage("Setup");
    private readonly NumericUpDown _poll = Ui.Number(1, 300);
    private readonly NumericUpDown _hysteresis = Ui.Number(0, 20);
    private readonly NumericUpDown _repeat = Ui.Number(0, 240);
    private readonly Label _hysteresisUnit = Ui.Caption("°C");
    private readonly ComboBox _sound = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = Ui.Scale(260) };
    private readonly Button _browseSound = new() { Text = "Choose .wav file...", AutoSize = true };
    private readonly Button _testSound = new() { Text = "Test sound", AutoSize = true };
    private readonly Label _soundFile = new() { AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(Ui.Scale(300), 0) };
    private readonly CheckBox _startup = new() { Text = "Start TAS when I sign in to Windows", AutoSize = true, Enabled = false };
    private readonly CheckBox _overlay = new() { Text = "Show a TAS pop-up when Windows hides notifications", AutoSize = true };
    private readonly CheckBox _fahrenheit = new() { Text = "Show temperatures in °F", AutoSize = true };
    private readonly CheckBox _csv = new() { Text = "Log readings to a CSV file", AutoSize = true };
    private readonly Button _rules = new() { Text = "Manage custom alerts...", AutoSize = true };
    private readonly Button _viewAvailable = new() { Text = "View Available Alerts", AutoSize = true };
    private readonly Label _rulesSummary = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(Ui.Scale(3), Ui.Scale(8), 0, 0) };
    private readonly ToolTip _tips = new();
    private readonly System.Windows.Forms.Timer _setupRefresh = new() { Interval = 3000 };
    private readonly Panel _statusBar = new() { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(Ui.Scale(12), Ui.Scale(4), Ui.Scale(12), Ui.Scale(8)) };
    private readonly Label _status = new()
    {
        AutoSize = true,
        Text = "Reading sensors...",
        ForeColor = SystemColors.GrayText,
        MaximumSize = new Size(Ui.Scale(700), 0),
    };

    private Size _contentSize;
    private bool _sized;
    private HardwareSnapshot? _snapshot;
    private bool _suppress;
    private bool _startupBusy;

    public event Action? InstallPawnIoRequested;
    public event Action? TestAlertRequested;
    public event Action? CpuThresholdChanged;
    public event Action? GpuThresholdChanged;
    public event Action? CustomAlertsChanged;
    public event Action? DisplayOptionsChanged;

    public string? LastAlert { get; set; }

    /// <summary>
    /// Shown in the status line while alerts are snoozed.
    /// </summary>
    public string? SnoozeStatus { get; set; }

    internal MainForm(Settings settings, HistoryStore history)
    {
        _settings = settings;
        _historyPanel = new HistoryPanel(settings, history);
        Ui.UseFahrenheit = settings.UseFahrenheit;

        Text = "TAS - Temperature Alert System";
        Font = new Font("Segoe UI", 9f);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;

        Ui.SetClamped(_poll, settings.PollIntervalSeconds);
        Ui.SetClamped(_repeat, settings.RepeatAlertMinutes);
        ApplyUnits();

        _sound.Items.AddRange(["Notification only (no extra sound)", "Warning chime", "Critical alarm (repeats 3 times)", "My own .wav file"]);
        _sound.SelectedIndex = Math.Clamp((int)settings.AlertSound, 0, _sound.Items.Count - 1);
        UpdateSoundControls();

        _startup.Checked = false;
        _fahrenheit.Checked = settings.UseFahrenheit;
        _overlay.Checked = settings.ShowOverlayAlerts;
        _csv.Checked = settings.LogReadingsToCsv;
        UpdateRulesSummary();
        _tips.SetToolTip(_startup, "Adds a Windows scheduled task that starts TAS in the tray at sign-in without a UAC prompt.");
        _tips.SetToolTip(_csv, $"Adds a row every check to {CsvLogger.FilePath} (temperatures in °C).");

        WireEvents();

        _tabs.TabPages.Add(BuildMonitorPage());
        _tabs.TabPages.Add(BuildAlertsPage());
        _tabs.TabPages.Add(_historyPage);
        _tabs.TabPages.Add(BuildSettingsPage());
        _tabs.TabPages.Add(_setupPage);
        _historyPage.Controls.Add(_historyPanel);
        _setupPage.Controls.Add(_setup);
        _setup.Dock = DockStyle.Top;
        UpdateSetupBadge();

        // Tabs don't size themselves to their pages, so work out the largest page once and size the window to fit.
        var needed = new Size(Ui.Scale(700), Ui.Scale(430));
        foreach (TabPage page in _tabs.TabPages)
        {
            if (page == _historyPage || page.Controls.Count == 0)
                continue;
            Size preferred = page.Controls[0].GetPreferredSize(Size.Empty);
            needed = new Size(Math.Max(needed.Width, preferred.Width), Math.Max(needed.Height, preferred.Height));
        }

        _contentSize = needed + new Size(Ui.Scale(20), Ui.Scale(20));

        _statusBar.Controls.Add(_status);
        Controls.Add(_tabs);
        Controls.Add(_statusBar);
    }

    private static TabPage NewPage(string title) => new(title)
    {
        UseVisualStyleBackColor = true,
        Padding = new Padding(Ui.Scale(10)),
        AutoScroll = true,
    };

    private void WireEvents()
    {
        _cpuPanel.ThresholdChanged += v =>
        {
            Apply(() => _settings.CpuThresholdC = v);
            CpuThresholdChanged?.Invoke();
        };
        _gpuPanel.ThresholdChanged += v =>
        {
            Apply(() => _settings.GpuThresholdC = v);
            GpuThresholdChanged?.Invoke();
        };
        _poll.ValueChanged += (_, _) => Apply(() => _settings.PollIntervalSeconds = (int)_poll.Value);
        _hysteresis.ValueChanged += (_, _) =>
        {
            if (!_suppress)
                Apply(() => _settings.HysteresisC = Ui.DeltaFromDisplay((float)_hysteresis.Value));
        };
        _repeat.ValueChanged += (_, _) => Apply(() => _settings.RepeatAlertMinutes = (int)_repeat.Value);

        _sound.SelectedIndexChanged += (_, _) => OnSoundChosen();
        _browseSound.Click += (_, _) =>
        {
            ChooseSoundFile();
            UpdateSoundControls();
        };
        _testSound.Click += (_, _) => AlertSounds.Play(_settings);

        _startup.CheckedChanged += async (_, _) => await OnStartupToggledAsync();
        _fahrenheit.CheckedChanged += (_, _) =>
        {
            Apply(() => _settings.UseFahrenheit = _fahrenheit.Checked);
            Ui.UseFahrenheit = _fahrenheit.Checked;
            ApplyUnits();
            DisplayOptionsChanged?.Invoke();
        };
        _csv.CheckedChanged += (_, _) => Apply(() => _settings.LogReadingsToCsv = _csv.Checked);
        _overlay.CheckedChanged += (_, _) => Apply(() => _settings.ShowOverlayAlerts = _overlay.Checked);
        _rules.Click += (_, _) => OpenRules();
        _viewAvailable.Click += (_, _) => OpenAvailableAlerts();

        _setup.InstallPawnIoRequested += () => InstallPawnIoRequested?.Invoke();
        _setup.TestAlertRequested += () => TestAlertRequested?.Invoke();
        _setup.GuideRequested += ShowHelp;
        _setup.AttentionChanged += UpdateSetupBadge;
        _setupRefresh.Tick += (_, _) => _setup.RefreshChecks();
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            if (_tabs.SelectedTab == _historyPage)
                _historyPanel.UpdateData();
        };
        VisibleChanged += (_, _) =>
        {
            _setupRefresh.Enabled = Visible;
            if (Visible)
            {
                _setup.RefreshChecks();
                _ = RefreshStartupAsync();
            }
        };
    }

    private TabPage BuildMonitorPage()
    {
        var devices = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
        };
        devices.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        devices.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        devices.Controls.Add(_cpuPanel, 0, 0);
        devices.Controls.Add(_gpuPanel, 1, 0);

        TabPage page = NewPage("Monitor");
        page.Controls.Add(devices);
        return page;
    }

    private TabPage BuildAlertsPage()
    {
        var timing = Grid();
        AddRow(timing, "Check the sensors every", _poll, Ui.Caption("seconds"));
        AddRow(timing, "Alert again after cooling by", _hysteresis, _hysteresisUnit);
        AddRow(timing, "Repeat the alert every", _repeat, Ui.Caption("minutes (0 = alert once)"));
        AddNote(timing, "After an alert, the temperature has to drop by the cooling amount before TAS alerts again. "
                        + "While it stays too hot, TAS reminds you at the repeat interval. Snooze alerts from the tray icon menu.");

        var sound = Grid();
        AddRow(sound, "Play", _sound, _testSound);
        var fileRow = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
        fileRow.Controls.Add(_browseSound);
        fileRow.Controls.Add(_soundFile);
        sound.Controls.Add(new Label { AutoSize = true });
        sound.Controls.Add(fileRow);
        sound.SetColumnSpan(fileRow, 2);
        AddNote(sound, "This plays as well as the Windows notification sound.");

        var custom = Grid();
        AddNote(custom, "Get an alert for any other reading, such as GPU hot spot, memory temperature, load, or a fan stopping.");
        var customRow = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
        customRow.Controls.Add(_rules);
        customRow.Controls.Add(_viewAvailable);
        customRow.Controls.Add(_rulesSummary);
        custom.Controls.Add(customRow);
        custom.SetColumnSpan(customRow, 3);

        TabPage page = NewPage("Alerts");
        page.Controls.Add(Stack(Box("When to alert", timing), Box("Alert sound", sound), Box("Custom alerts", custom)));
        return page;
    }

    private TabPage BuildSettingsPage()
    {
        var general = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(Ui.Scale(6)) };
        general.Controls.Add(Option(_startup, "TAS starts quietly in the tray, with administrator rights and no UAC prompt. Untick to remove it."));
        general.Controls.Add(Option(_overlay, "Windows hides its own notifications during games, full-screen apps and Do Not Disturb. In those cases TAS shows its own small alert in the top-right corner for 10 seconds. It never takes focus and clicks go through it. \"Send test alert\" always shows it."));
        general.Controls.Add(Option(_fahrenheit, "Only changes what TAS shows. Your limits are converted for you."));

        var openFolder = new Button { Text = "Open data folder", AutoSize = true, Margin = new Padding(Ui.Scale(18), Ui.Scale(2), 0, Ui.Scale(6)) };
        openFolder.Click += (_, _) => OpenFolder();
        var data = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(Ui.Scale(6)) };
        data.Controls.Add(Option(_csv, "Adds a row at every check (temperatures in °C), which you can open in Excel. The file is capped at about 10 MB."));
        data.Controls.Add(openFolder);

        TabPage page = NewPage("Settings");
        page.Controls.Add(Stack(Box("General", general), Box("Data", data)));
        return page;
    }

    private static TableLayoutPanel Grid() => new()
    {
        ColumnCount = 3,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = new Padding(Ui.Scale(6)),
    };

    private static void AddRow(TableLayoutPanel grid, string caption, Control input, Control? trailing)
    {
        Label label = Ui.Caption(caption);
        label.Margin = new Padding(0, Ui.Scale(6), Ui.Scale(16), Ui.Scale(6));
        grid.Controls.Add(label);
        grid.Controls.Add(input);
        grid.Controls.Add(trailing ?? new Label { AutoSize = true });
        input.Anchor = AnchorStyles.Left;
        input.Margin = new Padding(0, Ui.Scale(3), Ui.Scale(6), Ui.Scale(3));
    }

    private static void AddNote(TableLayoutPanel grid, string text)
    {
        var note = new Label
        {
            AutoSize = true,
            Text = text,
            ForeColor = SystemColors.GrayText,
            MaximumSize = new Size(Ui.Scale(620), 0),
            Margin = new Padding(0, Ui.Scale(4), 0, Ui.Scale(2)),
        };
        grid.Controls.Add(note);
        grid.SetColumnSpan(note, 3);
    }

    private static Control Option(CheckBox box, string description)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 0, 0, Ui.Scale(6)) };
        box.Margin = new Padding(Ui.Scale(3), Ui.Scale(3), 0, 0);
        panel.Controls.Add(box);
        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = description,
            ForeColor = SystemColors.GrayText,
            MaximumSize = new Size(Ui.Scale(600), 0),
            Margin = new Padding(Ui.Scale(21), 0, 0, 0),
        });
        return panel;
    }

    private static GroupBox Box(string title, Control content)
    {
        var box = new GroupBox
        {
            Text = title,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Padding = new Padding(Ui.Scale(8), Ui.Scale(4), Ui.Scale(8), Ui.Scale(6)),
            Margin = new Padding(0, 0, 0, Ui.Scale(8)),
        };
        // Docked content starts below the title; content that isn't docked would sit underneath it.
        content.Dock = DockStyle.Fill;
        box.Controls.Add(content);
        return box;
    }

    private static TableLayoutPanel Stack(params Control[] boxes)
    {
        var stack = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
        };
        foreach (Control box in boxes)
            stack.Controls.Add(box);
        return stack;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_sized)
            return;

        _sized = true;
        Size chrome = _tabs.Size - _tabs.DisplayRectangle.Size;
        ClientSize = new Size(_contentSize.Width + chrome.Width, _contentSize.Height + chrome.Height + _statusBar.Height);
        CenterToScreen();
    }

    public void ShowSnapshot(HardwareSnapshot snapshot)
    {
        _snapshot = snapshot;
        _cpuPanel.ShowDevice(snapshot.Cpu);
        _gpuPanel.ShowDevice(snapshot.Gpu);
        if (_tabs.SelectedTab == _historyPage)
            _historyPanel.UpdateData();

        string text = $"Last updated {DateTime.Now:T}";
        if (LastAlert is not null)
            text += $"   |   Last alert: {LastAlert}";
        if (SnoozeStatus is not null)
            text += $"   |   {SnoozeStatus}";
        _status.Text = text;
    }

    public void ShowError(string message) => _status.Text = $"Sensor read failed: {message}";

    public void ShowStatus(string message) => _status.Text = message;

    public void ShowPawnIoStatus(bool installing = false, string? error = null)
    {
        _setup.DriverError = error;
        _setup.Installing = installing;
    }

    public void ShowHelp()
    {
        using var help = new HelpForm();
        help.ShowDialog(Visible ? this : null);
    }

    public void ShowHistoryTab()
    {
        _tabs.SelectedTab = _historyPage;
        _historyPanel.UpdateData();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (!_settings.HasSeenHelp)
        {
            _settings.HasSeenHelp = true;
            _settings.Save();
            BeginInvoke(ShowHelp);
        }
    }

    private void UpdateSetupBadge() => _setupPage.Text = _setup.NeedsAttention ? "Setup \u26A0" : "Setup";

    /// <summary>
    /// Re-shows the limits and the alert cool-down in the current °C/°F unit without saving anything.
    /// </summary>
    private void ApplyUnits()
    {
        _suppress = true;
        try
        {
            _cpuPanel.Threshold = _settings.CpuThresholdC;
            _gpuPanel.Threshold = _settings.GpuThresholdC;
            _hysteresis.Maximum = (decimal)Math.Round(Ui.DeltaToDisplay(20));
            Ui.SetClamped(_hysteresis, (decimal)Math.Round(Ui.DeltaToDisplay(_settings.HysteresisC)));
            _hysteresisUnit.Text = Ui.UnitSymbol;
        }
        finally
        {
            _suppress = false;
        }
    }

    private void OnSoundChosen()
    {
        if (_suppress)
            return;

        var kind = (AlertSoundKind)_sound.SelectedIndex;
        if (kind == AlertSoundKind.CustomFile && !File.Exists(_settings.CustomSoundPath ?? "") && !ChooseSoundFile())
        {
            // Nothing chosen: go back to the previous sound.
            _suppress = true;
            _sound.SelectedIndex = (int)_settings.AlertSound;
            _suppress = false;
            return;
        }

        Apply(() => _settings.AlertSound = kind);
        UpdateSoundControls();
    }

    private bool ChooseSoundFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose an alert sound",
            Filter = "WAV sound (*.wav)|*.wav",
            CheckFileExists = true,
        };
        if (File.Exists(_settings.CustomSoundPath ?? ""))
            dialog.FileName = _settings.CustomSoundPath;

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return false;

        Apply(() => _settings.CustomSoundPath = dialog.FileName);
        return true;
    }

    private void UpdateSoundControls()
    {
        bool custom = _sound.SelectedIndex == (int)AlertSoundKind.CustomFile;
        _browseSound.Visible = custom;
        _soundFile.Visible = custom;
        _soundFile.Text = string.IsNullOrEmpty(_settings.CustomSoundPath) ? "(none chosen)" : Path.GetFileName(_settings.CustomSoundPath);
    }

    private async Task RefreshStartupAsync()
    {
        if (_startupBusy)
            return;

        _startupBusy = true;
        try
        {
            bool enabled = await Task.Run(StartupTask.IsEnabled);
            _suppress = true;
            _startup.Checked = enabled;
            _suppress = false;
            _startup.Enabled = true;
        }
        finally
        {
            _startupBusy = false;
        }
    }

    private async Task OnStartupToggledAsync()
    {
        if (_suppress || _startupBusy)
            return;

        _startupBusy = true;
        bool want = _startup.Checked;
        _startup.Enabled = false;
        try
        {
            string? error = await Task.Run(() => want ? StartupTask.Enable() : StartupTask.Disable());
            if (error is not null)
            {
                MessageBox.Show(this, $"Couldn't {(want ? "turn on" : "turn off")} starting with Windows:\n\n{error}", "TAS",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _suppress = true;
                _startup.Checked = !want;
                _suppress = false;
            }
        }
        finally
        {
            _startup.Enabled = true;
            _startupBusy = false;
        }
    }

    private void OpenRules()
    {
        using (var dialog = new RulesForm(_settings, _snapshot))
            dialog.ShowDialog(this);

        UpdateRulesSummary();
        CustomAlertsChanged?.Invoke();
    }

    private void OpenAvailableAlerts()
    {
        bool changed;
        using (var dialog = new AvailableAlertsForm(_settings, _snapshot))
        {
            dialog.ShowDialog(this);
            changed = dialog.AlertsChanged;
        }

        if (!changed)
            return;

        UpdateRulesSummary();
        CustomAlertsChanged?.Invoke();
    }

    private void UpdateRulesSummary()
    {
        int total = _settings.CustomAlerts.Count;
        int on = _settings.CustomAlerts.Count(r => r.Enabled);
        _rulesSummary.Text = total == 0 ? "None set up yet" : $"{total} set up, {on} switched on";
    }

    private static void OpenFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Path.GetDirectoryName(Settings.FilePath)!) { UseShellExecute = true });
        }
        catch (Exception)
        {
        }
    }

    private void Apply(Action change)
    {
        change();
        _settings.Save();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Closing the window keeps TAS running in the tray; use the tray menu to exit.
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _setupRefresh.Dispose();
            _tips.Dispose();
        }

        base.Dispose(disposing);
    }
}
