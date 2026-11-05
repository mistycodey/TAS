using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TAS;

public enum CheckState { Ok, Warning, Info }

public readonly record struct CheckResult(CheckState State, string Title, string Detail);

/// <summary>
/// Checks for the things a new user may need to set up outside TAS before alerts work.
/// </summary>
public static class SetupChecks
{
    public static CheckResult Driver(PawnIoSetup.Status status, bool installing)
    {
        if (installing)
            return new(CheckState.Info, "Installing PawnIO driver...", "Follow the PawnIO installer window. TAS restarts automatically when it finishes.");

        return status switch
        {
            PawnIoSetup.Status.Ready => new(CheckState.Ok, "Sensor driver installed",
                "PawnIO is installed, so TAS can read CPU temperatures."),
            PawnIoSetup.Status.Outdated => new(CheckState.Warning, "Sensor driver out of date",
                "Your PawnIO driver is too old to read CPU temperatures. Click \"Update now\" (one-time, a few seconds)."),
            _ => new(CheckState.Warning, "Sensor driver needed for CPU temperatures",
                "Windows doesn't let apps read CPU temperatures without the free PawnIO driver. Click \"Install now\" (one-time, a few seconds). GPU readings work without it."),
        };
    }

    public static CheckResult Notifications()
    {
        if (ReadDword(@"Software\Microsoft\Windows\CurrentVersion\PushNotifications", "ToastEnabled") == 0)
        {
            return new(CheckState.Warning, "Windows notifications are turned off",
                "TAS alerts won't appear. Click \"Notification settings\" and turn on Notifications.");
        }

        if (ReadDword(@"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings", "NOC_GLOBAL_SETTING_TOASTS_ENABLED") == 0)
        {
            return new(CheckState.Warning, "Do Not Disturb may be on",
                "Alerts will go silently to the notification centre instead of popping up. Turn off Do Not Disturb in \"Notification settings\".");
        }

        if (SHQueryUserNotificationState(out NotificationState state) == 0)
        {
            switch (state)
            {
                case NotificationState.RunningFullScreen or NotificationState.Busy:
                    return new(CheckState.Info, "Full-screen app running",
                        "Windows holds pop-up notifications while a game or full-screen app is in front. Alerts will still be in the notification centre.");
                case NotificationState.PresentationMode:
                    return new(CheckState.Info, "Presentation mode is on",
                        "Windows is holding notifications until presentation mode ends.");
                case NotificationState.QuietTime:
                    return new(CheckState.Info, "Windows quiet time",
                        "Windows is temporarily holding notifications (for example right after an update).");
            }
        }

        return new(CheckState.Ok, "Notifications enabled",
            "Windows will show TAS alerts. Use \"Send test alert\" to check, and make sure TAS is allowed if it's listed in Notification settings.");
    }

    public static CheckResult TrayIcon()
    {
        bool? pinned = IsTrayIconPinned();
        return pinned switch
        {
            true => new(CheckState.Ok, "Tray icon visible", "The TAS icon is shown on the taskbar."),
            false => new(CheckState.Warning, "Tray icon hidden",
                "Windows has put the TAS icon behind the ^ arrow on the taskbar. To keep it visible, click \"Taskbar settings\", open \"Other system tray icons\" and switch TAS on."),
            null => new(CheckState.Info, "Tray icon",
                "If you can't see the TAS icon on the taskbar, click the ^ arrow next to the clock."),
        };
    }

    // Windows 11 records each tray icon under HKCU\Control Panel\NotifyIconSettings with an IsPromoted flag
    // (1 = shown on the taskbar). Returns null where that information isn't available, e.g. Windows 10.
    private static bool? IsTrayIconPinned()
    {
        try
        {
            using RegistryKey? root = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings");
            if (root is null || Environment.ProcessPath is not string exe)
                return null;

            foreach (string name in root.GetSubKeyNames())
            {
                using RegistryKey? icon = root.OpenSubKey(name);
                if (icon?.GetValue("ExecutablePath") is string path && string.Equals(path, exe, StringComparison.OrdinalIgnoreCase))
                    return icon.GetValue("IsPromoted") is int promoted && promoted == 1;
            }
        }
        catch (Exception)
        {
        }

        return null;
    }

    private static int? ReadDword(string keyPath, string valueName)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(valueName) as int?;
    }

    private enum NotificationState
    {
        NotPresent = 1,
        Busy = 2,
        RunningFullScreen = 3,
        PresentationMode = 4,
        AcceptsNotifications = 5,
        QuietTime = 6,
        App = 7,
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out NotificationState state);
}
