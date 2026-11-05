using System.Media;

namespace TAS;

/// <summary>
/// Plays the sound TAS adds on top of the Windows notification sound.
/// </summary>
internal static class AlertSounds
{
    // Held in a field so the player isn't garbage collected while it's still playing.
    private static SoundPlayer? _custom;

    public static void Play(Settings settings)
    {
        try
        {
            switch (settings.AlertSound)
            {
                case AlertSoundKind.Warning:
                    SystemSounds.Exclamation.Play();
                    break;
                case AlertSoundKind.Critical:
                    PlayCritical();
                    break;
                case AlertSoundKind.CustomFile:
                    PlayCustom(settings.CustomSoundPath);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Write($"Could not play alert sound: {ex.Message}");
        }
    }

    private static void PlayCritical()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    SystemSounds.Hand.Play();
                    await Task.Delay(900);
                }
            }
            catch (Exception ex)
            {
                Log.Write($"Could not play alert sound: {ex.Message}");
            }
        });
    }

    private static void PlayCustom(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Log.Write($"Custom alert sound not found ({path ?? "none chosen"}); played the warning chime instead.");
            SystemSounds.Exclamation.Play();
            return;
        }

        _custom?.Stop();
        var player = new SoundPlayer(path);
        player.Load(); // throws for anything that isn't a valid .wav
        _custom = player;
        player.Play();
    }
}
