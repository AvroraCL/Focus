using System.Windows;
using System.Windows.Media;

namespace Focus.App.Controls;

/// <summary>Per-core usage strip: one vertical bar per logical core.</summary>
public sealed class CoreUsageStrip : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(double[]), typeof(CoreUsageStrip),
        new FrameworkPropertyMetadata(Array.Empty<double>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public double[] Values
    {
        get => (double[])GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var values = Values;
        if (values is null || values.Length == 0 || ActualWidth <= 0 || ActualHeight <= 0) return;

        double gap = 3;
        double barWidth = Math.Max(2, (ActualWidth - gap * (values.Length - 1)) / values.Length);
        var track = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
        track.Freeze();
        var fill = new SolidColorBrush(Color.FromRgb(0x4C, 0x9A, 0xFF));
        fill.Freeze();
        var hot = new SolidColorBrush(Color.FromRgb(0xF0, 0xB4, 0x5E));
        hot.Freeze();

        for (int i = 0; i < values.Length; i++)
        {
            double x = i * (barWidth + gap);
            dc.DrawRoundedRectangle(track, null, new Rect(x, 0, barWidth, ActualHeight), 2, 2);
            double ratio = Math.Clamp(values[i] / 100.0, 0, 1);
            if (ratio <= 0.001) continue;
            double h = Math.Max(2, ActualHeight * ratio);
            dc.DrawRoundedRectangle(ratio > 0.9 ? hot : fill, null, new Rect(x, ActualHeight - h, barWidth, h), 2, 2);
        }
    }
}
