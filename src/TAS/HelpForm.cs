using System.Diagnostics;

namespace TAS;

/// <summary>
/// In-app user guide. Keep in sync with README.md.
/// </summary>
internal sealed class HelpForm : Form
{
    private readonly RichTextBox _text = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        BorderStyle = BorderStyle.None,
        BackColor = SystemColors.Window,
        DetectUrls = true,
    };

    public HelpForm()
    {
        Text = "TAS - Help & setup guide";
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(Ui.Scale(640), Ui.Scale(680));
        MinimumSize = new Size(Ui.Scale(420), Ui.Scale(360));
        ShowInTaskbar = false;

        _text.LinkClicked += (_, e) => Open(e.LinkText!);
        BuildContent();

        var openFolder = new Button { Text = "Open settings && log folder", AutoSize = true };
        openFolder.Click += (_, _) => Open(Path.GetDirectoryName(Settings.FilePath)!);
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK };
        CancelButton = close;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(Ui.Scale(8)),
        };
        buttons.Controls.Add(close);
        buttons.Controls.Add(openFolder);

        var textHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Ui.Scale(16), Ui.Scale(12), Ui.Scale(8), 0), BackColor = SystemColors.Window };
        textHost.Controls.Add(_text);

        Controls.Add(textHost);
        Controls.Add(buttons);
    }

    private void BuildContent()
    {
        Heading("TAS - Temperature Alert System", 14f);
        Paragraph("TAS watches your CPU and GPU temperatures and shows a Windows notification when either one goes above the limit you set. It runs in the system tray; closing the window keeps it running. Right-click the tray icon and choose Exit to quit.");

        Heading("Getting started");
        Paragraph("The Setup tab of the TAS window checks each of these for you (a warning sign on the tab means something needs doing) and shows a button for anything that needs doing.");

        SubHeading("1. Allow administrator access");
        Paragraph("Windows asks \"Do you want to allow this app to make changes?\" every time TAS starts. Click Yes. TAS needs administrator access to read hardware sensors; it does not change any system settings.");

        SubHeading("2. Install the PawnIO sensor driver (for CPU temperatures)");
        Paragraph("Windows doesn't let apps read CPU temperatures directly. TAS uses PawnIO, a small free, signed driver, to do this. GPU temperatures work without it.");
        Bullet("On the Setup tab, click \"Install now\".");
        Bullet("The PawnIO installer opens. Follow its steps (usually just Install, then Close).");
        Bullet("TAS restarts by itself and the CPU temperature appears within a few seconds.");
        Paragraph("You only need to do this once per PC. PawnIO is also used by LibreHardwareMonitor and FanControl. To remove it later, uninstall \"PawnIO\" from Settings > Apps > Installed apps. More information: https://pawnio.eu");

        SubHeading("3. Make sure Windows 11 shows notifications");
        Paragraph("TAS alerts are standard Windows notifications. They pop up in the bottom-right corner and stay in the notification centre (click the clock) if you miss them. Windows may hide them when:");
        Bullet("Notifications are turned off: Settings > System > Notifications > Notifications must be On.");
        Bullet("Do Not Disturb is on: it's on the same page, and in the notification centre. Alerts are then delivered silently to the notification centre.");
        Bullet("Do Not Disturb turns on automatically when you're gaming, using an app full-screen or duplicating your display. Check \"Turn on do not disturb automatically\" on the Notifications settings page if you want alerts during games.");
        Bullet("TAS is turned off in the list of notification senders. After the first alert, TAS may appear in that list; make sure it's On.");
        Paragraph("Click \"Send test alert\" on the Setup tab to check. If the Setup tab says notifications are enabled but nothing appears, open the notification centre: if the test alert is there, Do Not Disturb or Focus held it back.");

        SubHeading("4. Keep the tray icon visible (optional)");
        Paragraph("Windows 11 hides new tray icons behind the ^ arrow next to the clock. The TAS icon is a green dot that turns red when a temperature is over its limit. To keep it on the taskbar: Settings > Personalization > Taskbar > Other system tray icons, then switch TAS on. The \"Taskbar settings\" button opens that page.");

        Heading("Setting alerts");
        Bullet("Alert when above: the temperature, in °C, that triggers an alert. CPU and GPU each have their own limit. Changes are saved straight away and apply on the next check.");
        Bullet("Check every: how often TAS reads the sensors.");
        Bullet("Re-alert after cooling by: after an alert, the temperature must drop this many degrees below the limit before TAS alerts again. This stops a temperature hovering at the limit from sending an alert every few seconds.");
        Bullet("Repeat alert every: while it stays too hot, TAS reminds you this often. Set to 0 for a single alert.");
        Paragraph("TAS uses the hottest sensor on each device (shown under the big temperature) and lists every temperature sensor below it. Sensors at or above your limit are shown in red.");
        Paragraph("Typical limits: most desktop CPUs and GPUs throttle themselves at around 90-100°C. A limit of 80-85°C for the CPU and 80°C for the GPU gives you an early warning. GPU memory junction and hot spot readings normally run 10-20°C hotter than the GPU core.");

        Heading("Extra features");
        Bullet("Alert sound: TAS can play a warning chime, a critical alarm (three beeps) or your own .wav file as well as the Windows notification. Use \"Test sound\" to hear it.");
        Bullet("Custom alerts: click \"Custom alerts...\" to get an alert on any other reading, such as GPU hot spot or memory temperature, load, or a fan dropping to 0 RPM. Pick the device, the sensor, \"goes above\" or \"drops below\", and a limit. Sensors only appear once TAS has read them.");
        Bullet("Snooze: right-click the tray icon > Snooze alerts to pause alerts for 30 minutes, 1 hour or 2 hours (for example while gaming or benchmarking). Alerts resume by themselves, and anything still too hot alerts straight away.");
        Bullet("Temperature history: the History tab shows a graph of the last 10 minutes, 30 minutes or hour, with your limits as dashed lines, and the peak temperatures since TAS started.");
        Bullet("Show temperatures in °F: changes what TAS displays. Your limits are converted for you.");
        Bullet("Log readings to a CSV file: adds a row at every check to readings.csv (temperatures in °C), which you can open in Excel. At 10 MB the file is moved to readings.csv.old.");
        Bullet("Start TAS when I sign in: adds a Windows scheduled task that starts TAS in the tray with administrator rights and no UAC prompt. Untick it to remove the task.");

        Heading("Troubleshooting");
        Bullet("CPU shows n/a: install PawnIO (see step 2). If it's installed, restart TAS.");
        Bullet("No alert appeared: check step 3, then look in the log file (below). Every alert TAS sends is recorded there, so if it's in the log, Windows hid it.");
        Bullet("A value shows n/a: not every CPU or GPU reports every sensor.");

        Heading("Files");
        Paragraph($"Settings: {Settings.FilePath}");
        Paragraph($"Alert log: {Log.FilePath}");
        Paragraph($"Readings (if logging is on): {CsvLogger.FilePath}");

        _text.Select(0, 0);
    }

    private void Heading(string text, float size = 12f) => Append(text + "\n", new Font(Font.FontFamily, size, FontStyle.Bold), before: 10);

    private void SubHeading(string text) => Append(text + "\n", new Font(Font, FontStyle.Bold), before: 6);

    private void Paragraph(string text) => Append(text + "\n", Font, before: 2);

    private void Bullet(string text)
    {
        _text.SelectionStart = _text.TextLength;
        _text.SelectionBullet = true;
        _text.SelectionIndent = Ui.Scale(8);
        Append(text + "\n", Font, before: 0);
        _text.SelectionBullet = false;
        _text.SelectionIndent = 0;
    }

    private void Append(string text, Font font, int before)
    {
        _text.SelectionStart = _text.TextLength;
        _text.SelectionLength = 0;
        if (before > 0)
        {
            _text.SelectionFont = new Font(Font.FontFamily, before);
            _text.AppendText("\n");
        }

        _text.SelectionStart = _text.TextLength;
        _text.SelectionFont = font;
        _text.AppendText(text);
    }

    private static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception)
        {
        }
    }
}
