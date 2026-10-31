using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Focus.App.Controls;

/// <summary>Downsampled frame-time chart with stutter highlighting and axis labels.</summary>
public sealed class FrameTimeChart : FrameworkElement
{
    private const int MaxBuckets = 1600;

    public double[] Values { get; set; } = Array.Empty<double>();
    public double MedianMs { get; set; }
    public double StutterThresholdMs { get; set; }
    public Color LineColor { get; set; } = Color.FromRgb(0x4C, 0x9A, 0xFF);
    public Color SpikeColor { get; set; } = Color.FromRgb(0xFF, 0x6B, 0x6B);

    public void SetData(double[] values, double medianMs, double thresholdMs)
    {
        Values = values;
        MedianMs = medianMs;
        StutterThresholdMs = thresholdMs;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 20 || h <= 20) return;

        var fg = new SolidColorBrush(Color.FromRgb(0x68, 0x71, 0x7E));
        fg.Freeze();
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)), 1);
        gridPen.Freeze();

        if (Values.Length < 2)
        {
            var lilHint = new FormattedText("—", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 14, fg, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(lilHint, new Point(w / 2 - 6, h / 2 - 8));
            return;
        }

        // Downsample into min/max/avg buckets.
        int buckets = Math.Min(MaxBuckets, Math.Max(64, (int)(w * 1.5)));
        int stride = Math.Max(1, Values.Length / buckets);
        int count = (int)Math.Ceiling(Values.Length / (double)stride);
        var min = new double[count];
        var max = new double[count];
        var avg = new double[count];

        for (int b = 0; b < count; b++)
        {
            int start = b * stride;
            int end = Math.Min(Values.Length, start + stride);
            double lo = double.MaxValue, hi = double.MinValue, sum = 0;
            for (int i = start; i < end; i++)
            {
                double v = Values[i];
                if (v < lo) lo = v;
                if (v > hi) hi = v;
                sum += v;
            }
            int n = Math.Max(1, end - start);
            min[b] = lo;
            max[b] = hi;
            avg[b] = sum / n;
        }

        double cap = 0;
        foreach (var v in max) if (v > cap) cap = v;
        double ceiling = Math.Max(StutterThresholdMs > 0 ? StutterThresholdMs * 1.35 : 0, Percentile(max, 0.995) * 1.2);
        if (ceiling <= 0) ceiling = cap > 0 ? cap : 33;
        ceiling = Math.Min(Math.Max(ceiling, 1), cap > 0 ? Math.Max(cap, 1) : 1);

        const double leftGutter = 46;
        const double topPad = 8;
        double plotW = w - leftGutter - 8;
        double plotH = h - topPad - 18;
        if (plotW <= 4 || plotH <= 4) return;

        double X(int i) => leftGutter + plotW * (count <= 1 ? 0 : i / (count - 1.0));
        double Y(double v) => topPad + plotH - Math.Clamp(v / ceiling, 0, 1) * plotH;

        // horizontal grid + labels
        for (int i = 0; i <= 2; i++)
        {
            double value = ceiling * (2 - i) / 2.0;
            double y = topPad + plotH * i / 2.0;
            dc.DrawLine(gridPen, new Point(leftGutter, y), new Point(leftGutter + plotW, y));
            var label = new FormattedText(
                value.ToString("F1", CultureInfo.CurrentCulture) + " ms",
                CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Cascadia Mono, Consolas"), 9.5, fg,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(label, new Point(2, y - label.Height / 2));
        }

        // band (min..max per bucket)
        var band = new StreamGeometry();
        using (var ctx = band.Open())
        {
            ctx.BeginFigure(new Point(X(0), Y(max[0])), true, true);
            for (int i = 1; i < count; i++) ctx.LineTo(new Point(X(i), Y(max[i])), true, false);
            for (int i = count - 1; i >= 0; i--) ctx.LineTo(new Point(X(i), Y(min[i])), true, false);
        }
        band.Freeze();
        var bandBrush = new SolidColorBrush(LineColor) { Opacity = 0.22 };
        bandBrush.Freeze();
        dc.DrawGeometry(bandBrush, null, band);

        // average line
        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(new Point(X(0), Y(avg[0])), false, false);
            for (int i = 1; i < count; i++) ctx.LineTo(new Point(X(i), Y(avg[i])), true, false);
        }
        line.Freeze();
        var linePen = new Pen(new SolidColorBrush(LineColor), 1.2);
        linePen.Freeze();
        dc.DrawGeometry(null, linePen, line);

        // stutter markers
        if (StutterThresholdMs > 0)
        {
            var spikePen = new Pen(new SolidColorBrush(SpikeColor), 1);
            spikePen.Freeze();
            for (int i = 0; i < count; i++)
            {
                if (max[i] < StutterThresholdMs) continue;
                double x = X(i);
                dc.DrawLine(spikePen, new Point(x, Y(max[i])), new Point(x, topPad + plotH));
            }
        }

        var footer = new FormattedText(
            $"{Values.Length:N0} frames · median {MedianMs:F1} ms",
            CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 10, fg, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(footer, new Point(leftGutter, h - footer.Height - 2));
    }

    private static double Percentile(double[] data, double p)
    {
        if (data.Length == 0) return 0;
        var copy = (double[])data.Clone();
        Array.Sort(copy);
        int index = Math.Clamp((int)(p * (copy.Length - 1)), 0, copy.Length - 1);
        return copy[index];
    }
}
