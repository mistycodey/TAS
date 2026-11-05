namespace TAS;

internal static class Log
{
    private const long MaxBytes = 1024 * 1024;

    public static string FilePath { get; } = Path.Combine(Path.GetDirectoryName(Settings.FilePath)!, "tas.log");

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes)
                File.Delete(FilePath);
            File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
    }
}
