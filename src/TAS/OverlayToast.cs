using System.Runtime.InteropServices;

namespace TAS;

/// <summary>
/// A small always-on-top alert drawn by TAS itself. Windows holds back its own toasts while a game or other full-screen
/// app is in front, but a topmost window that never takes focus still shows over borderless / windowed-fullscreen games.
/// It is click-through, so it can't interfere with the game's input.
/// </summary>
internal sealed class OverlayToast : Form
{
    private const int WsExTransparent = 0x20;
    private const int WsExToolWindow = 0x80;
    private const int WsExLayered = 0x80000;
    private const int WsExTopmost = 0x8;
    private const int WsExNoActivate = 0x08000000;

    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;

    private static readonly List<OverlayToast> Active = new();

    private readonly string _title;
    private readonly string _text;
    private readonly Color _accent;
    private readonly Font _titleFont = new("Segoe UI", 10.5f, FontStyle.Bold);
    private readonly Font _textFont = new("Segoe UI", 9.5f);
    private readonly System.Windows.Forms.Timer _life;
    private readonly System.Windows.Forms.Timer _keepOnTop = new() { Interval = 500 };
    private readonly Screen _screen;
    private readonly Image? _image;
    private readonly Rectangle _imageBounds;
    private readonly int _textLeft;
    private readonly int _textTop;
    private readonly int _textWidth;
    private readonly int _titleHeight;

    private OverlayToast(string title, string text, Color accent, int seconds, Screen screen, Image? image, int imageSize)
    {
        _title = title;
        _text = text;
        _accent = accent;
        _screen = screen;
        _image = image;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(32, 32, 36);
        Opacity = 0.96;

        // Title and message sit above the picture so the alert is still readable when the picture is large.
        int padding = Ui.Scale(12);
        int left = Ui.Scale(6 + 14);
        int gap = Ui.Scale(10);
        int chrome = padding * 2; // top and bottom padding; image gap added only when there is a picture

        Size shown = Size.Empty;
        if (image is not null && image.Width > 0 && image.Height > 0)
        {
            int box = Math.Min(Ui.Scale(imageSize), (int)Math.Min(screen.Bounds.Height * 0.8, screen.Bounds.Width * 0.5));
            double scale = Math.Min(8.0, Math.Min((double)box / image.Width, (double)box / image.Height));
            shown = new Size(Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale)));
        }

        int maxTextWidth = Math.Max(Ui.Scale(360), shown.Width);
        Size titleSize = TextRenderer.MeasureText(title, _titleFont, new Size(maxTextWidth, int.MaxValue), TextFormatFlags.WordBreak);
        Size textSize = TextRenderer.MeasureText(text, _textFont, new Size(maxTextWidth, int.MaxValue), TextFormatFlags.WordBreak);
        _textWidth = Math.Max(Math.Max(titleSize.Width, textSize.Width), shown.Width);
        _titleHeight = titleSize.Height;
        int textBlock = titleSize.Height + Ui.Scale(4) + textSize.Height;

        // Shrink the picture if the whole toast wouldn't fit on the screen with the message above it.
        if (shown != Size.Empty)
        {
            int maxImageHeight = screen.Bounds.Height - Ui.Scale(32) - chrome - gap - textBlock;
            if (shown.Height > maxImageHeight && maxImageHeight > Ui.Scale(80))
            {
                double shrink = (double)maxImageHeight / shown.Height;
                shown = new Size(Math.Max(1, (int)(shown.Width * shrink)), Math.Max(1, (int)(shown.Height * shrink)));
            }
        }

        _textLeft = left;
        _textTop = padding;
        if (shown != Size.Empty)
            _imageBounds = new Rectangle(left + (_textWidth - shown.Width) / 2, padding + textBlock + gap, shown.Width, shown.Height);

        int height = padding + textBlock + (shown == Size.Empty ? 0 : gap + shown.Height) + padding;
        ClientSize = new Size(left + _textWidth + Ui.Scale(16), height);

        _life = new System.Windows.Forms.Timer { Interval = Math.Max(1, seconds) * 1000 };
        _life.Tick += (_, _) => Close();
        _keepOnTop.Tick += (_, _) => AssertTopmost();
    }

    /// <summary>
    /// Shows an alert in the top-right corner of the screen the foreground window (e.g. the game) is on.
    /// </summary>
    public static void Display(string title, string text, Color accent, int seconds = 10, Image? image = null, int imageSize = 720)
    {
        Screen screen = Screen.FromHandle(GetForegroundWindow());
        var toast = new OverlayToast(title, text, accent, seconds, screen, image, imageSize);
        toast.FormClosed += (_, _) =>
        {
            Active.Remove(toast);
            Arrange();
        };
        Active.Add(toast);
        Arrange();
        toast.Show();
        toast.AssertTopmost();
        toast._life.Start();
        toast._keepOnTop.Start();
    }

    public static void CloseAll()
    {
        foreach (OverlayToast toast in Active.ToList())
            toast.Close();
    }

    // Stacks the toasts downwards from the top-right corner, per screen.
    private static void Arrange()
    {
        int margin = Ui.Scale(16);
        int gap = Ui.Scale(8);
        var nextY = new Dictionary<string, int>();
        foreach (OverlayToast toast in Active)
        {
            Rectangle area = toast._screen.Bounds;
            int y = nextY.TryGetValue(toast._screen.DeviceName, out int stacked) ? stacked : area.Top + margin;
            toast.Location = new Point(area.Right - toast.Width - margin, y);
            nextY[toast._screen.DeviceName] = y + toast.Height + gap;
            if (toast.IsHandleCreated)
                toast.AssertTopmost();
        }
    }

    // Games can reclaim the topmost slot, so this is repeated while the toast is up.
    private void AssertTopmost()
    {
        if (IsHandleCreated && !IsDisposed)
            SetWindowPos(Handle, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= WsExTopmost | WsExToolWindow | WsExNoActivate | WsExTransparent | WsExLayered;
            return cp;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using (var bar = new SolidBrush(_accent))
            e.Graphics.FillRectangle(bar, 0, 0, Ui.Scale(6), Height);

        if (_image is not null && !_imageBounds.IsEmpty)
        {
            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(_image, _imageBounds);
        }

        const TextFormatFlags flags = TextFormatFlags.WordBreak | TextFormatFlags.Left | TextFormatFlags.Top;
        TextRenderer.DrawText(e.Graphics, _title, _titleFont, new Rectangle(_textLeft, _textTop, _textWidth, Height), Color.White, flags);
        TextRenderer.DrawText(e.Graphics, _text, _textFont, new Rectangle(_textLeft, _textTop + _titleHeight + Ui.Scale(4), _textWidth, Height), Color.Gainsboro, flags);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _life.Dispose();
            _keepOnTop.Dispose();
            _titleFont.Dispose();
            _textFont.Dispose();
            _image?.Dispose();
        }

        base.Dispose(disposing);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
}
