using System.Diagnostics;
using Microsoft.Win32;

namespace TAS;

/// <summary>
/// Detects and installs the PawnIO driver, which LibreHardwareMonitor needs to read CPU sensors.
/// </summary>
public static class PawnIoSetup
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";
    private const string ResourceName = "TAS.Resources.PawnIO_setup.exe";
    private static readonly Version MinimumVersion = new(2, 0, 0, 0);

    public enum Status { Ready, Missing, Outdated }

    // LibreHardwareMonitor's own PawnIo.IsInstalled is cached for the life of the process,
    // so read the registry directly to see the result of an install we just ran.
    public static Status GetStatus()
    {
        Version? version = ReadVersion(RegistryView.Default) ?? ReadVersion(RegistryView.Registry64);
        if (version is null)
            return Status.Missing;
        return version < MinimumVersion ? Status.Outdated : Status.Ready;
    }

    /// <summary>
    /// Runs the bundled PawnIO installer and waits for it to finish. Returns the driver status afterwards.
    /// </summary>
    public static async Task<Status> InstallAsync()
    {
        string setupPath = Path.Combine(Path.GetTempPath(), "TAS", "PawnIO_setup.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(setupPath)!);

        await using (Stream resource = typeof(PawnIoSetup).Assembly.GetManifestResourceStream(ResourceName)
                                       ?? throw new InvalidOperationException("PawnIO installer is missing from this build."))
        await using (FileStream file = File.Create(setupPath))
        {
            await resource.CopyToAsync(file);
        }

        try
        {
            using Process process = Process.Start(new ProcessStartInfo(setupPath, "-install") { UseShellExecute = true })
                                    ?? throw new InvalidOperationException("Could not start the PawnIO installer.");
            await process.WaitForExitAsync();
        }
        finally
        {
            try { File.Delete(setupPath); } catch (IOException) { }
        }

        return GetStatus();
    }

    private static Version? ReadVersion(RegistryView view)
    {
        using RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
        using RegistryKey? key = root.OpenSubKey(UninstallKey);
        return Version.TryParse(key?.GetValue("DisplayVersion") as string, out Version? version) ? version : null;
    }
}
