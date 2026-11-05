namespace TAS;

internal static class Ui
{
    private static readonly float DpiScale = GetDpiScale();

    /// <summary>
    /// Converts a size designed at 100% Windows scaling into pixels for the current display scaling.
    /// </summary>
    public static int Scale(int pixels) => (int)Math.Round(pixels * DpiScale);

    public static NumericUpDown Number(int min, int max) =>
        new() { Minimum = min, Maximum = max, Width = Scale(64), TextAlign = HorizontalAlignment.Right };

    public static void SetClamped(NumericUpDown input, decimal value) =>
        input.Value = Math.Clamp(value, input.Minimum, input.Maximum);

    /// <summary>
    /// Display unit. All stored limits and logged values stay in °C; only what the user sees is converted.
    /// </summary>
    public static bool UseFahrenheit { get; set; }

    public static string UnitSymbol => UseFahrenheit ? "°F" : "°C";

    public static float ToDisplay(float celsius) => UseFahrenheit ? celsius * 9f / 5f + 32f : celsius;

    public static float FromDisplay(float shown) => UseFahrenheit ? (shown - 32f) * 5f / 9f : shown;

    /// <summary>
    /// Converts a temperature difference (no 32-degree offset).
    /// </summary>
    public static float DeltaToDisplay(float celsius) => UseFahrenheit ? celsius * 9f / 5f : celsius;

    public static float DeltaFromDisplay(float shown) => UseFahrenheit ? shown * 5f / 9f : shown;

    public static string Temp(float? value) => value is float v ? $"{ToDisplay(v):0}{UnitSymbol}" : "n/a";

    public static Label Caption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, Scale(6), Scale(6), Scale(6)),
    };

    public static Color TemperatureColor(float? value, float threshold)
    {
        if (value is not float v)
            return SystemColors.GrayText;
        if (v >= threshold)
            return Color.Firebrick;
        if (v >= threshold - 10)
            return Color.DarkOrange;
        return Color.SeaGreen;
    }

    private static float GetDpiScale()
    {
        using Graphics g = Graphics.FromHwnd(IntPtr.Zero);
        return g.DpiX / 96f;
    }
}
