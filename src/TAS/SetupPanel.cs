using System.Diagnostics;

namespace TAS;

/// <summary>
/// Checklist at the top of the main window showing what's set up and offering a fix for anything that isn't.
/// </summary>
internal sealed class SetupPanel : GroupBox
{
    private readonly Row _driver;
    private readonly Row _notifications;
    private readonly Row _tray;
    private readonly Button _installDriver = ActionButton("Install now");
    private bool _installing;

    public event Action? InstallPawnIoRequested;
    public event Action? TestAlertRequested;
    public event Action? GuideRequested;
    public event Action? AttentionChanged;

    /// <summary>
    /// True when the driver or Windows notifications need fixing before alerts will work properly.
    /// </summary>
    public bool NeedsAttention { get; private set; }

    public SetupPanel()
    {
        Text = "Setup";
        Dock = DockStyle.Fill;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(Ui.Scale(8), Ui.Scale(4), Ui.Scale(8), Ui.Scale(4));

        Button testAlert = ActionButton("Send test alert");
        Button notificationSettings = ActionButton("Notification settings");
        Button taskbarSettings = ActionButton("Taskbar settings");
        Button help = ActionButton("Help && setup guide");

        _installDriver.Click += (_, _) => InstallPawnIoRequested?.Invoke();
        testAlert.Click += (_, _) => TestAlertRequested?.Invoke();
        notificationSettings.Click += (_, _) => OpenUri("ms-settings:notifications");
        taskbarSettings.Click += (_, _) => OpenUri("ms-settings:taskbar");
        help.Click += (_, _) => GuideRequested?.Invoke();

        var table = new TableLayoutPanel
        {
            ColumnCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _driver = new Row(table, _installDriver);
        _notifications = new Row(table, testAlert, notificationSettings);
        _tray = new Row(table, taskbarSettings);

        var helpRow = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Right, Margin = Padding.Empty };
        helpRow.Controls.Add(help);
        table.Controls.Add(helpRow);
        table.SetColumnSpan(helpRow, 3);

        Controls.Add(table);
        RefreshChecks();
    }

    public bool Installing
    {
        get => _installing;
        set
        {
            _installing = value;
            RefreshChecks();
        }
    }

    public string? DriverError { get; set; }

    public void RefreshChecks()
    {
        PawnIoSetup.Status driverStatus = PawnIoSetup.GetStatus();
        CheckResult driver = SetupChecks.Driver(driverStatus, _installing);
        if (DriverError is not null && driverStatus != PawnIoSetup.Status.Ready && !_installing)
            driver = driver with { Detail = $"{driver.Detail}\nLast attempt failed: {DriverError}" };

        _driver.Show(driver);
        _installDriver.Visible = driverStatus != PawnIoSetup.Status.Ready && !_installing;
        _installDriver.Text = driverStatus == PawnIoSetup.Status.Outdated ? "Update now" : "Install now";

        CheckResult notifications = SetupChecks.Notifications();
        _notifications.Show(notifications);

        bool attention = driver.State == CheckState.Warning || notifications.State == CheckState.Warning;
        if (attention != NeedsAttention)
        {
            NeedsAttention = attention;
            AttentionChanged?.Invoke();
        }

        CheckResult tray = SetupChecks.TrayIcon();
        _tray.Show(tray);
        _tray.SetActionsVisible(tray.State != CheckState.Ok);
    }

    private static void OpenUri(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't open Windows Settings: {ex.Message}", "TAS", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static Button ActionButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(Ui.Scale(4), Ui.Scale(2), 0, Ui.Scale(2)),
    };

    private sealed class Row
    {
        private readonly Label _icon = new()
        {
            AutoSize = true,
            Font = new Font("Segoe UI Symbol", 12f, FontStyle.Bold),
            Margin = new Padding(0, Ui.Scale(4), Ui.Scale(6), Ui.Scale(4)),
        };
        private readonly Label _title = new() { AutoSize = true, Margin = new Padding(0, Ui.Scale(5), 0, 0) };
        private readonly Label _detail = new()
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            MaximumSize = new Size(Ui.Scale(470), 0),
            Margin = new Padding(0, Ui.Scale(1), 0, Ui.Scale(5)),
        };
        private readonly FlowLayoutPanel _actions = new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Anchor = AnchorStyles.Right,
            Margin = Padding.Empty,
        };

        public Row(TableLayoutPanel table, params Button[] actions)
        {
            _title.Font = new Font(_title.Font, FontStyle.Bold);

            var text = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = Padding.Empty,
            };
            text.Controls.Add(_title);
            text.Controls.Add(_detail);
            _actions.Controls.AddRange(actions);

            table.Controls.Add(_icon);
            table.Controls.Add(text);
            table.Controls.Add(_actions);
        }

        public void Show(CheckResult result)
        {
            (_icon.Text, _icon.ForeColor) = result.State switch
            {
                CheckState.Ok => ("\u2714", Color.SeaGreen),
                CheckState.Warning => ("\u26A0", Color.DarkOrange),
                _ => ("\u2139", Color.SteelBlue),
            };
            _title.Text = result.Title;
            _detail.Text = result.Detail;
        }

        public void SetActionsVisible(bool visible) => _actions.Visible = visible;
    }
}
