using System.Windows;
using System.Windows.Media;

namespace Focus.App.Controls;

/// <summary>Compact live line chart used on the overview page.</summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(float[]), typeof(Sparkline),
        new FrameworkPropertyMetadata(Array.Empty<float>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public float[] Values
    {
        get => (float[])GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Color StrokeColor { get; set; } = Color.FromRgb(0xDB, 0x30, 0x24);
    public double StrokeThickness { get; set; } = 1.5;
    public bool FillArea { get; set; } = true;
    public double FillOpacity { get; set; } = 0.16;
    public bool FixedZero { get; set; } = true;
    public double FixedMax { get; set; }

    protected override void OnRender(DrawingContext dc)
    {
        var values = Values;
        if (values is null || values.Length < 2 || ActualWidth <= 1 || ActualHeight <= 1) return;

        double min = double.MaxValue, max = double.MinValue;
        foreach (var v in values)
        {
            if (float.IsNaN(v)) continue;
            if (v < min) min = v;
            if (v > max) max = v;
        }
        if (min > max) return;
        if (FixedMax > 0) max = FixedMax;
        if (FixedZero) min = 0;
        if (max - min < 0.01) max = min + 1;

        double w = ActualWidth, h = ActualHeight;
        double X(int i) => w * i / (values.Length - 1.0);
        double Y(double v) => h - Math.Clamp((v - min) / (max - min), 0, 1.2) * h;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(0, h), FillArea, true);
            ctx.LineTo(new Point(0, Y(values[0])), true, false);
            for (int i = 1; i < values.Length; i++) ctx.LineTo(new Point(X(i), Y(values[i])), true, false);
            ctx.LineTo(new Point(w, h), true, false);
        }
        geometry.Freeze();

        if (FillArea)
        {
            var fillColor = StrokeColor;
            fillColor.A = (byte)(255 * FillOpacity);
            var fill = new SolidColorBrush(fillColor);
            fill.Freeze();
            dc.DrawGeometry(fill, null, geometry);
        }

        var lineGeometry = new StreamGeometry();
        using (var ctx = lineGeometry.Open())
        {
            ctx.BeginFigure(new Point(0, Y(values[0])), false, false);
            for (int i = 1; i < values.Length; i++) ctx.LineTo(new Point(X(i), Y(values[i])), true, false);
        }
        lineGeometry.Freeze();

        var pen = new Pen(new SolidColorBrush(StrokeColor), StrokeThickness) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        dc.DrawGeometry(null, pen, lineGeometry);
    }
}

/// <summary>Rounded usage bar with an optional peak marker.</summary>
public sealed class MetricBar : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(MetricBar),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Maximum { get; set; } = 100;
    public Color TrackColor { get; set; } = Color.FromRgb(0xDE, 0xDD, 0xD6);
    public Color FillColor { get; set; } = Color.FromRgb(0xDB, 0x30, 0x24);
    public double Radius { get; set; } = 0;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        dc.DrawRoundedRectangle(new SolidColorBrush(TrackColor), null, new Rect(0, 0, w, h), Radius, Radius);

        double ratio = Maximum > 0 ? Math.Clamp(Value / Maximum, 0, 1) : 0;
        if (ratio <= 0) return;
        double fillWidth = Math.Max(h, w * ratio);
        var brush = new SolidColorBrush(FillColor);
        brush.Freeze();
        dc.DrawRoundedRectangle(brush, null, new Rect(0, 0, fillWidth, h), Radius, Radius);
    }
}
