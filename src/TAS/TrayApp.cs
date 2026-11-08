using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace TAS;

public sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _cpuItem = new("CPU: --") { Enabled = false };
    private readonly ToolStripMenuItem _gpuItem = new("GPU: --") { Enabled = false };
    private readonly ToolStripMenuItem _resumeItem;
    private readonly AlertTracker _cpuAlert = new();
    private readonly AlertTracker _gpuAlert = new();
    private readonly Dictionary<AlertRule, AlertTracker> _ruleAlerts = new();
    private readonly HistoryStore _history = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Settings _settings = Settings.Load();
    private readonly MainForm _window;
    private HardwareReader? _reader;
    private HardwareSnapshot? _lastSnapshot;
    private DateTime? _snoozeUntil;
    private Icon? _currentIcon;
    private bool? _currentlyHot;

    public TrayApp()
    {
        var menu = new ContextMenuStrip();
        var openItem = new ToolStripMenuItem("Open TAS", null, (_, _) => OpenWindow()) { Font = new Font(menu.Font, FontStyle.Bold) };
        menu.Items.Add(openItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_cpuItem);
        menu.Items.Add(_gpuItem);
        menu.Items.Add(new ToolStripSeparator());

        var snoozeMenu = new ToolStripMenuItem("Snooze alerts");
        snoozeMenu.DropDownItems.Add("For 30 minutes", null, (_, _) => Snooze(TimeSpan.FromMinutes(30)));
        snoozeMenu.DropDownItems.Add("For 1 hour", null, (_, _) => Snooze(TimeSpan.FromHours(1)));
        snoozeMenu.DropDownItems.Add("For 2 hours", null, (_, _) => Snooze(TimeSpan.FromHours(2)));
        snoozeMenu.DropDownItems.Add(new ToolStripSeparator());
        _resumeItem = new ToolStripMenuItem("Resume alerts", null, (_, _) => Resume()) { Enabled = false };
        snoozeMenu.DropDownItems.Add(_resumeItem);
        menu.Items.Add(snoozeMenu);

        menu.Items.Add("Temperature history", null, (_, _) => ShowHistory());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Help && setup guide", null, (_, _) => ShowHelp());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        _tray = new NotifyIcon
        {
            Text = "TAS - starting...",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => OpenWindow();
        _tray.BalloonTipClicked += (_, _) => OpenWindow();
        SetIcon(hot: false);

        _window = new MainForm(_settings, _history) { Icon = _currentIcon };
        _window.InstallPawnIoRequested += () => _ = InstallPawnIoAsync();
        _window.CpuThresholdChanged += _cpuAlert.Reset;
        _window.GpuThresholdChanged += _gpuAlert.Reset;
        _window.CustomAlertsChanged += _ruleAlerts.Clear;
        _window.DisplayOptionsChanged += RefreshDisplay;
        _window.TestAlertRequested += () =>
        {
            ShowToast("TAS test alert", "If you can see this, temperature alerts will appear here.", ToolTipIcon.Warning, forceOverlay: true);
            AlertSounds.Play(_settings);
            _window.ShowStatus($"Test alert sent at {DateTime.Now:T}. If nothing popped up, check the notification centre and see Help.");
        };

        // Started by the "start with Windows" task: stay in the tray until the user opens the window.
        if (!Program.StartInTray)
            OpenWindow();

        _ = RunAsync(_cts.Token);
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            _reader = await Task.Run(() => new HardwareReader(), token);
        }
        catch (Exception ex)
        {
            _window.ShowError(ex.Message);
            ShowToast("TAS could not start sensor monitoring", ex.Message, ToolTipIcon.Error);
            return;
        }

        if (PawnIoSetup.GetStatus() != PawnIoSetup.Status.Ready)
        {
            ShowToast("CPU temperature unavailable",
                "Click here, then \"Install now\", to add the PawnIO driver TAS needs for CPU sensors.", ToolTipIcon.Warning);
        }

        while (!token.IsCancellationRequested)
        {
            try
            {
                HardwareSnapshot snapshot = await Task.Run(_reader.Read, token);
                OnReading(snapshot);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _tray.Text = Truncate($"TAS - read error: {ex.Message}");
                _window.ShowError(ex.Message);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _settings.PollIntervalSeconds)), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void OnReading(HardwareSnapshot s)
    {
        _lastSnapshot = s;
        _history.Add(s);
        if (_settings.LogReadingsToCsv)
            CsvLogger.Append(s);

        if (_snoozeUntil is DateTime until && DateTime.Now >= until)
            Resume();

        UpdateDisplay(s);

        // While snoozed nothing is evaluated, so anything still too hot alerts straight away once alerts resume.
        if (_snoozeUntil is null)
        {
            CheckTemperatureLimits(s);
            CheckCustomAlerts(s);
        }

        if (_window.Visible)
            _window.ShowSnapshot(s);
    }

    private void UpdateDisplay(HardwareSnapshot s)
    {
        float? cpuTemp = s.Cpu?.Temperature;
        float? gpuTemp = s.Gpu?.Temperature;

        _cpuItem.Text = $"CPU: {Ui.Temp(cpuTemp)}  (alert at {Ui.Temp(_settings.CpuThresholdC)})";
        _gpuItem.Text = $"GPU: {Ui.Temp(gpuTemp)}  (alert at {Ui.Temp(_settings.GpuThresholdC)})";
        _tray.Text = Truncate((_snoozeUntil is DateTime until ? $"TAS (alerts snoozed until {until:t})" : "TAS")
                              + $"\nCPU {Ui.Temp(cpuTemp)}\nGPU {Ui.Temp(gpuTemp)}");

        bool hot = cpuTemp >= _settings.CpuThresholdC || gpuTemp >= _settings.GpuThresholdC;
        SetIcon(hot);
    }

    private void CheckTemperatureLimits(HardwareSnapshot s)
    {
        float? cpuTemp = s.Cpu?.Temperature;
        float? gpuTemp = s.Gpu?.Temperature;

        if (_cpuAlert.ShouldAlert(cpuTemp, _settings.CpuThresholdC, _settings.HysteresisC, _settings.RepeatAlertMinutes))
        {
            _window.LastAlert = $"CPU {Ui.Temp(cpuTemp)} at {DateTime.Now:T}";
            Alert("CPU temperature high",
                $"{s.Cpu!.Name} is at {Ui.Temp(cpuTemp)} (alert at {Ui.Temp(_settings.CpuThresholdC)})");
        }

        if (_gpuAlert.ShouldAlert(gpuTemp, _settings.GpuThresholdC, _settings.HysteresisC, _settings.RepeatAlertMinutes))
        {
            _window.LastAlert = $"GPU {Ui.Temp(gpuTemp)} at {DateTime.Now:T}";
            Alert("GPU temperature high",
                $"{s.Gpu!.Name} is at {Ui.Temp(gpuTemp)} (alert at {Ui.Temp(_settings.GpuThresholdC)})");
        }
    }

    private void CheckCustomAlerts(HardwareSnapshot s)
    {
        List<AlertRule> rules = _settings.CustomAlerts.ToList();
        foreach (AlertRule stale in _ruleAlerts.Keys.Where(r => !rules.Contains(r)).ToList())
            _ruleAlerts.Remove(stale);

        foreach (AlertRule rule in rules.Where(r => r.Enabled))
        {
            DeviceSnapshot? device = rule.Device == AlertDevice.Cpu ? s.Cpu : s.Gpu;
            SensorReading? reading = rule.Find(device);
            if (!_ruleAlerts.TryGetValue(rule, out AlertTracker? tracker))
                _ruleAlerts[rule] = tracker = new AlertTracker();

            // The tracker alerts when a value reaches a limit, so "below" is checked on the negated values.
            bool below = rule.Comparison == AlertComparison.Below;
            float? value = reading?.Value is float v ? (below ? -v : v) : null;
            float limit = below ? -rule.Limit : rule.Limit;
            if (!tracker.ShouldAlert(value, limit, rule.Hysteresis, _settings.RepeatAlertMinutes))
                continue;

            string shown = SensorInfo.Format(rule.SensorType, reading!.Value.Value);
            _window.LastAlert = $"{rule.DeviceName} {rule.SensorName} {shown} at {DateTime.Now:T}";
            Alert($"{rule.DeviceName} {rule.SensorName} alert",
                $"{device!.Name}: {rule.SensorName} is {shown} ({(below ? "below" : "above")} {SensorInfo.Format(rule.SensorType, rule.Limit)})");
        }
    }

    private void Alert(string title, string text)
    {
        ShowToast(title, text, ToolTipIcon.Warning);
        AlertSounds.Play(_settings);
    }

    private void Snooze(TimeSpan duration)
    {
        _snoozeUntil = DateTime.Now + duration;
        _resumeItem.Enabled = true;
        _window.SnoozeStatus = $"Alerts snoozed until {_snoozeUntil:t}";
        Log.Write($"Alerts snoozed until {_snoozeUntil:T}");
        RefreshDisplay();
    }

    private void Resume()
    {
        if (_snoozeUntil is null)
            return;

        _snoozeUntil = null;
        _resumeItem.Enabled = false;
        _window.SnoozeStatus = null;
        Log.Write("Alerts resumed");

        _cpuAlert.Reset();
        _gpuAlert.Reset();
        _ruleAlerts.Clear();
        RefreshDisplay();
    }

    /// <summary>
    /// Redraws everything that shows a temperature or the snooze state, e.g. after switching °C/°F.
    /// </summary>
    private void RefreshDisplay()
    {
        if (_lastSnapshot is null)
            return;

        UpdateDisplay(_lastSnapshot);
        if (_window.Visible)
            _window.ShowSnapshot(_lastSnapshot);
    }

    private async Task InstallPawnIoAsync()
    {
        _window.ShowPawnIoStatus(installing: true);
        PawnIoSetup.Status status;
        try
        {
            status = await PawnIoSetup.InstallAsync();
        }
        catch (Exception ex)
        {
            _window.ShowPawnIoStatus(error: ex.Message);
            return;
        }

        if (status != PawnIoSetup.Status.Ready)
        {
            _window.ShowPawnIoStatus(error: "the installer was cancelled or did not complete.");
            return;
        }

        // The sensor library only detects PawnIO at process start, so relaunch to pick it up.
        _settings.Save();
        Program.RestartRequested = true;
        ExitThread();
    }

    private void ShowHelp() => _window.ShowHelp();

    private void ShowHistory()
    {
        OpenWindow();
        _window.ShowHistoryTab();
    }

    private void OpenWindow()
    {
        if (_lastSnapshot is not null)
            _window.ShowSnapshot(_lastSnapshot);
        _window.Show();
        if (_window.WindowState == FormWindowState.Minimized)
            _window.WindowState = FormWindowState.Normal;
        _window.Activate();
    }

    // The Windows notification is always sent (it also lands in the notification centre). TAS's own pop-up is only added
    // when Windows is holding notifications back, e.g. during a game, so normally there's just one alert on screen.
    private void ShowToast(string title, string text, ToolTipIcon icon, bool forceOverlay = false)
    {
        Log.Write($"Notification: {title} - {text}");
        _tray.ShowBalloonTip(10_000, title, text, icon);

        if (_settings.ShowOverlayAlerts && (forceOverlay || SetupChecks.WindowsHoldsToasts()))
        {
            Color accent = icon == ToolTipIcon.Error ? Color.Firebrick : Color.OrangeRed;
            try
            {
                OverlayToast.Display(title, text, accent);
            }
            catch (Exception ex)
            {
                Log.Write($"Overlay alert failed: {ex.Message}");
            }
        }
    }

    private void SetIcon(bool hot)
    {
        if (_currentlyHot == hot && _currentIcon is not null)
            return;
        _currentlyHot = hot;

        Icon? previous = _currentIcon;
        _currentIcon = CreateDotIcon(hot ? Color.OrangeRed : Color.MediumSeaGreen);
        _tray.Icon = _currentIcon;
        if (_window is not null)
            _window.Icon = _currentIcon;
        if (previous is not null)
            DestroyIcon(previous);
    }

    private static Icon CreateDotIcon(Color color)
    {
        using var bmp = new Bitmap(32, 32);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var fill = new SolidBrush(color);
            using var outline = new Pen(Color.White, 2);
            g.FillEllipse(fill, 3, 3, 26, 26);
            g.DrawEllipse(outline, 3, 3, 26, 26);
        }

        return Icon.FromHandle(bmp.GetHicon());
    }

    private static void DestroyIcon(Icon icon)
    {
        DestroyIcon(icon.Handle);
        icon.Dispose();
    }

    [DllImport("user32.dll", EntryPoint = "DestroyIcon")]
    private static extern bool DestroyIcon(IntPtr handle);

    // NotifyIcon.Text throws above 127 characters.
    private static string Truncate(string text) => text.Length <= 127 ? text : text[..127];

    protected override void ExitThreadCore()
    {
        _cts.Cancel();
        OverlayToast.CloseAll();
        _tray.Visible = false;
        _tray.Dispose();
        _window.Dispose();
        _reader?.Dispose();
        if (_currentIcon is not null)
            DestroyIcon(_currentIcon);
        base.ExitThreadCore();
    }
}
