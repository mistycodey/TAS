using System.Drawing.Drawing2D;

namespace TAS;

/// <summary>
/// Line chart of CPU and GPU temperature over time, with each device's alert limit as a dashed line.
/// </summary>
internal sealed class HistoryChart : Control
{
    private static readonly Color CpuColor = Color.DodgerBlue;
    private static readonly Color GpuColor = Color.SeaGreen;

    private HistoryStore.Sample[] _samples = [];
    private TimeSpan _window = TimeSpan.FromMinutes(10);
    private float _cpuLimit;
    private float _gpuLimit;

    public HistoryChart()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = SystemColors.Window;
        Size = new Size(Ui.Scale(660), Ui.Scale(300));
    }

    public void SetData(HistoryStore.Sample[] samples, TimeSpan window, float cpuLimitC, float gpuLimitC)
    {
        _samples = samples;
        _window = window;
        _cpuLimit = cpuLimitC;
        _gpuLimit = gpuLimitC;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int left = Ui.Scale(54), top = Ui.Scale(30), right = Ui.Scale(16), bottom = Ui.Scale(30);
        var plot = new Rectangle(left, top, Width - left - right, Height - top - bottom);
        if (plot.Width < 40 || plot.Height < 40)
            return;

        DateTime end = DateTime.Now;
        DateTime start = end - _window;
        HistoryStore.Sample[] visible = _samples.Where(s => s.Time >= start).ToArray();

        // Vertical range in the display unit, rounded out to whole grid steps.
        var values = new List<float> { Ui.ToDisplay(_cpuLimit), Ui.ToDisplay(_gpuLimit) };
        foreach (HistoryStore.Sample s in visible)
        {
            if (s.Cpu is float c) values.Add(Ui.ToDisplay(c));
            if (s.Gpu is float gv) values.Add(Ui.ToDisplay(gv));
        }

        float range = values.Max() - values.Min() + 10;
        float step = range <= 60 ? 10 : range <= 120 ? 20 : 40;
        float lo = MathF.Floor((values.Min() - 5) / step) * step;
        float hi = MathF.Ceiling((values.Max() + 5) / step) * step;
        if (hi - lo < step * 2)
            hi = lo + step * 2;

        float X(DateTime t) => plot.Left + (float)((t - start).TotalSeconds / _window.TotalSeconds) * plot.Width;
        float Y(float shown) => plot.Bottom - (shown - lo) / (hi - lo) * plot.Height;

        using var gridPen = new Pen(Color.Gainsboro);
        using var textBrush = new SolidBrush(SystemColors.GrayText);
        using var rightAlign = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
        using var centerAlign = new StringFormat { Alignment = StringAlignment.Center };

        for (float v = lo; v <= hi + 0.01f; v += step)
        {
            float y = Y(v);
            g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            g.DrawString($"{v:0}{Ui.UnitSymbol}", Font, textBrush, new RectangleF(0, y - 10, plot.Left - 6, 20), rightAlign);
        }

        for (int i = 0; i <= 4; i++)
        {
            float fraction = i / 4f;
            float x = plot.Left + fraction * plot.Width;
            g.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);
            string label = i == 4 ? "now" : $"-{Math.Round(_window.TotalMinutes * (1 - fraction)):0} min";
            g.DrawString(label, Font, textBrush, new RectangleF(x - 40, plot.Bottom + 4, 80, 20), centerAlign);
        }

        using var border = new Pen(Color.Silver);
        g.DrawRectangle(border, plot);

        Region? oldClip = g.Clip;
        g.SetClip(plot);
        DrawLimit(g, Y(Ui.ToDisplay(_cpuLimit)), plot, CpuColor);
        DrawLimit(g, Y(Ui.ToDisplay(_gpuLimit)), plot, GpuColor);
        DrawSeries(g, visible, s => s.Cpu, X, Y, CpuColor);
        DrawSeries(g, visible, s => s.Gpu, X, Y, GpuColor);
        g.Clip = oldClip;

        DrawLegend(g, plot);
    }

    private static void DrawLimit(Graphics g, float y, Rectangle plot, Color color)
    {
        using var pen = new Pen(Color.FromArgb(140, color), 1.5f) { DashStyle = DashStyle.Dash };
        g.DrawLine(pen, plot.Left, y, plot.Right, y);
    }

    private static void DrawSeries(
        Graphics g,
        HistoryStore.Sample[] samples,
        Func<HistoryStore.Sample, float?> pick,
        Func<DateTime, float> x,
        Func<float, float> y,
        Color color)
    {
        using var pen = new Pen(color, 2f) { LineJoin = LineJoin.Round };
        using var brush = new SolidBrush(color);
        var run = new List<PointF>();

        void Flush()
        {
            if (run.Count == 1)
                g.FillEllipse(brush, run[0].X - 2.5f, run[0].Y - 2.5f, 5, 5);
            else if (run.Count > 1)
                g.DrawLines(pen, run.ToArray());
            run.Clear();
        }

        foreach (HistoryStore.Sample s in samples)
        {
            if (pick(s) is float value)
                run.Add(new PointF(x(s.Time), y(Ui.ToDisplay(value))));
            else
                Flush();
        }

        Flush();
    }

    private void DrawLegend(Graphics g, Rectangle plot)
    {
        using var textBrush = new SolidBrush(ForeColor);
        int x = plot.Left;
        int y = Ui.Scale(8);
        foreach ((string name, Color color) in new[] { ("CPU", CpuColor), ("GPU", GpuColor) })
        {
            using var pen = new Pen(color, 3f);
            g.DrawLine(pen, x, y + 8, x + 18, y + 8);
            g.DrawString(name, Font, textBrush, x + 22, y);
            x += Ui.Scale(70);
        }

        using var gray = new SolidBrush(SystemColors.GrayText);
        using var right = new StringFormat { Alignment = StringAlignment.Far };
        g.DrawString("Dashed lines are your alert limits", Font, gray, new RectangleF(plot.Left, y, plot.Width, 20), right);
    }
}
