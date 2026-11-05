# TAS - Temperature Alert System

TAS is a small Windows program that watches your CPU and GPU temperatures and shows a Windows notification when either one goes above a limit you set. It runs in the system tray. Closing its window doesn't stop it; use **Exit** in the tray icon's right-click menu.

## Download

Get `TAS.exe` from the releases page:

**https://github.com/OWNER/TAS/releases/latest** *(replace `OWNER` with the GitHub account that hosts this repository)*

It's a single file. There's nothing to install, and you don't need .NET. The same file is in the `release` folder of this project.

## Requirements

- Windows 10 or 11, 64-bit.
- Administrator rights. Windows asks you to confirm every time TAS starts, because reading hardware sensors needs it.
- For CPU temperatures, the free PawnIO driver. TAS can install it for you (see First run). GPU temperatures work without it.

## First run

1. Run `TAS.exe` and click **Yes** on the Windows prompt.
2. Open the **Setup** tab. A warning sign on the tab means something needs fixing.
3. If it says the sensor driver is needed, click **Install now**. TAS restarts when it's done.
4. Click **Send test alert** to check that Windows shows the notification.

## What it does

- Reads the CPU and GPU temperature every 5 seconds (you can change this).
- Shows a notification when a temperature reaches its limit. The CPU and GPU each have their own limit. The defaults are 85°C for the CPU and 80°C for the GPU.
- Doesn't repeat itself every few seconds. After an alert, the temperature has to drop by a set amount before TAS alerts again. If it stays too hot, TAS reminds you every 10 minutes. You can change both, or set the reminder to 0 for a single alert.
- Can play a sound as well: a warning chime, a critical alarm, or a `.wav` file you choose.
- **Custom alerts**: alert on any other reading the hardware reports, such as GPU hot spot, memory temperature, load or fan speed. You choose "goes above" or "drops below" and a limit.
- **Snooze**: right-click the tray icon, then **Snooze alerts**, to pause alerts for 30 minutes, 1 hour or 2 hours.
- **History**: a graph of the last 10 minutes, 30 minutes or hour, with your limits drawn on it, plus the highest temperatures since TAS started. History isn't kept when TAS exits.
- Shows temperatures in °C or °F.
- Can log every reading to a CSV file you can open in Excel.
- Can start when you sign in to Windows, with no prompt. It does this with a scheduled task that you can remove by unticking the option.
- Shows every temperature sensor for the CPU and GPU, and marks the ones over your limit.

The tray icon is a green dot that turns red when a temperature is over its limit.

## If something doesn't work

- **The CPU shows n/a.** Install the PawnIO driver from the Setup tab. If it's already installed, restart TAS.
- **No notification appears.** Windows may be hiding it: check that notifications are on and Do Not Disturb is off. Games and full-screen apps can turn Do Not Disturb on by themselves. Held notifications still appear in the notification centre (click the clock). TAS also writes every alert to its log file.
- **A value shows n/a.** Not every CPU or GPU reports every sensor.

## Files

| File | Location |
| --- | --- |
| Settings | `%APPDATA%\TAS\settings.json` |
| Alert log | `%APPDATA%\TAS\tas.log` |
| Readings (only if CSV logging is on) | `%APPDATA%\TAS\readings.csv` |

## Building from source

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
cd src\TAS
dotnet build
```

To produce the single-file release:

```powershell
cd src\TAS
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o ..\..\release
```

## Third-party components

- [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (MPL-2.0) reads the hardware sensors.
- The [PawnIO](https://pawnio.eu) installer (version 2.2.0) is embedded in TAS so it can install the driver for you. Check PawnIO's licence terms before you redistribute TAS.
