using System.Diagnostics;

namespace TAS;

internal static class Program
{
    /// <summary>
    /// Command-line switch used by the start-with-Windows task: start in the tray without opening the window.
    /// </summary>
    public const string TrayArgument = "--tray";

    public static bool RestartRequested { get; set; }

    public static bool StartInTray { get; private set; }

    [STAThread]
    private static void Main(string[] args)
    {
        StartInTray = args.Contains(TrayArgument, StringComparer.OrdinalIgnoreCase);

        using var mutex = new Mutex(false, @"Global\TAS.TemperatureAlertSystem");
        if (!AcquireSingleInstance(mutex))
            return;

        try
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new TrayApp());
        }
        finally
        {
            mutex.ReleaseMutex();
        }

        if (RestartRequested)
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true });
    }

    private static bool AcquireSingleInstance(Mutex mutex)
    {
        try
        {
            // Brief wait so a restarting instance can take over from the one that is exiting.
            return mutex.WaitOne(TimeSpan.FromSeconds(5));
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }
}
