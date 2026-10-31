using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Focus.Core.Profiles;

namespace Focus.Hud.Rendering;

internal static class HudPainting
{
    private static readonly ConcurrentDictionary<uint, SolidColorBrush> BrushCache = new();
    private static readonly ConcurrentDictionary<(uint Color, int Thickness), Pen> PenCache = new();

    public static Color ParseColor(string? value, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(value);
            return color;
        }
        catch
        {
            return fallback;
        }
    }

    public static SolidColorBrush Brush(string? value, double opacity = 1.0)
    {
        var color = ParseColor(value, Colors.White);
        if (opacity < 1) color.A = (byte)Math.Clamp(color.A * opacity, 0, 255);
        uint key = ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
        return BrushCache.GetOrAdd(key, _ =>
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        });
    }

    public static Pen Pen(string? value, double thickness, double opacity = 1.0)
    {
        if (thickness <= 0) thickness = 1;
        var color = ParseColor(value, Colors.White);
        if (opacity < 1) color.A = (byte)Math.Clamp(color.A * opacity, 0, 255);
        uint colorKey = ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
        int thicknessKey = (int)Math.Round(thickness * 4);
        return PenCache.GetOrAdd((colorKey, thicknessKey), key =>
        {
            var pen = new Pen(Brush(value, opacity), key.Thickness / 4.0);
            pen.Freeze();
            return pen;
        });
    }

    public static FormattedText Text(
        string text,
        double fontSize,
        string family,
        int weight,
        bool italic,
        Brush brush,
        double pixelsPerDip,
        HudTextAlign alignment = HudTextAlign.Left)
    {
        if (fontSize <= 0) fontSize = 12;
        var typeface = new Typeface(
            new FontFamily(string.IsNullOrWhiteSpace(family) ? "Segoe UI" : family),
            italic ? FontStyles.Italic : FontStyles.Normal,
            FontWeight.FromOpenTypeWeight(Math.Clamp(weight, 1, 999)),
            FontStretches.Normal);

        var formatted = new FormattedText(
            string.IsNullOrEmpty(text) ? " " : text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            brush,
            pixelsPerDip);

        formatted.TextAlignment = alignment switch
        {
            HudTextAlign.Center => TextAlignment.Center,
            HudTextAlign.Right => TextAlignment.Right,
            _ => TextAlignment.Left,
        };
        formatted.Trimming = TextTrimming.CharacterEllipsis;
        return formatted;
    }
}
