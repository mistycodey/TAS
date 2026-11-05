using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace TAS;

/// <summary>
/// Starts TAS at logon through a scheduled task. A normal Run-key startup entry can't launch an app that
/// requires administrator rights without a UAC prompt; a task set to "run with highest privileges" can.
/// </summary>
internal static class StartupTask
{
    private const string TaskName = "TAS Temperature Alert System";

    public static bool IsEnabled() => Run("/Query", "/TN", TaskName).ExitCode == 0;

    /// <summary>
    /// Returns null on success, otherwise a message describing what went wrong.
    /// </summary>
    public static string? Enable()
    {
        string? exe = Environment.ProcessPath;
        if (exe is null)
            return "Couldn't work out where TAS.exe is.";

        string xml = BuildTaskXml(exe, WindowsIdentity.GetCurrent().Name);
        string temp = Path.Combine(Path.GetTempPath(), $"tas-task-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(temp, xml, Encoding.Unicode);
            (int exitCode, string output) = Run("/Create", "/TN", TaskName, "/XML", temp, "/F");
            return exitCode == 0 ? null : output;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
        finally
        {
            try { File.Delete(temp); } catch (IOException) { }
        }
    }

    public static string? Disable()
    {
        (int exitCode, string output) = Run("/Delete", "/TN", TaskName, "/F");
        return exitCode == 0 || !IsEnabled() ? null : output;
    }

    private static string BuildTaskXml(string exe, string user)
    {
        string u = SecurityElement.Escape(user);
        string command = SecurityElement.Escape($"\"{exe}\"");
        string folder = SecurityElement.Escape(Path.GetDirectoryName(exe) ?? "");
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Starts TAS (Temperature Alert System) in the system tray when you sign in.</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{u}</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{u}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <StartWhenAvailable>true</StartWhenAvailable>
                <Enabled>true</Enabled>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{command}</Command>
                  <Arguments>{Program.TrayArgument}</Arguments>
                  <WorkingDirectory>{folder}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static (int ExitCode, string Output) Run(params string[] args)
    {
        try
        {
            var info = new ProcessStartInfo("schtasks.exe")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (string arg in args)
                info.ArgumentList.Add(arg);

            using Process process = Process.Start(info)!;
            string error = process.StandardError.ReadToEnd();
            string output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(15_000))
            {
                process.Kill();
                return (-1, "schtasks.exe didn't respond.");
            }

            return (process.ExitCode, (error + output).Trim());
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
