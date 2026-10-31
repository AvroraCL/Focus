using System.Windows;
using System.Windows.Media;
using Focus.Core.Metrics;
using Focus.Core.Profiles;
using Focus.Hud.Formatting;

namespace Focus.Hud.Rendering;

/// <summary>
/// The single rendering path shared by the editor preview and the in-game overlay. Drawing happens in
/// canvas coordinates; callers apply the display transform.
/// </summary>
public static class HudRenderer
{
    public static void Render(DrawingContext dc, HudProfile profile, IHudDataSource data, double pixelsPerDip, bool dimUnavailable = false)
    {
        bool pushed = false;
        if (profile.Display.Opacity < 0.999)
        {
            dc.PushOpacity(Math.Clamp(profile.Display.Opacity, 0, 1));
            pushed = true;
        }

        foreach (var element in profile.Elements)
        {
            if (!element.Visible) continue;
            RenderElement(dc, element, data, pixelsPerDip, dimUnavailable);
        }

        if (pushed) dc.Pop();
    }

    public static void RenderElement(DrawingContext dc, HudElement element, IHudDataSource data, double pixelsPerDip, bool dimUnavailable = false)
    {
        if (element.Width <= 0 || element.Height <= 0) return;

        var rect = new Rect(element.X, element.Y, element.Width, element.Height);
        bool pushedOpacity = false;
        if (element.Opacity < 0.999)
        {
            dc.PushOpacity(Math.Clamp(element.Opacity, 0, 1));
            pushedOpacity = true;
        }

        double radius = Math.Min(element.CornerRadius, Math.Min(rect.Width, rect.Height) / 2);

        if (!string.IsNullOrWhiteSpace(element.Background))
        {
            var color = HudConditions.Resolve(element, ColorTarget.Background, data, element.Background!);
            dc.DrawRoundedRectangle(
                HudPainting.Brush(color, element.BackgroundOpacity),
                null, rect, radius, radius);
        }

        if (element.BorderWidth > 0 && !string.IsNullOrWhiteSpace(element.BorderColor))
        {
            var color = HudConditions.Resolve(element, ColorTarget.Border, data, element.BorderColor!);
            dc.DrawRoundedRectangle(null, HudPainting.Pen(color, element.BorderWidth), rect, radius, radius);
        }

        var inner = new Rect(
            rect.X + Math.Max(0, element.PaddingLeft),
            rect.Y + Math.Max(0, element.PaddingTop),
            Math.Max(0, rect.Width - element.PaddingLeft - element.PaddingRight),
            Math.Max(0, rect.Height - element.PaddingTop - element.PaddingBottom));

        if (inner.Width <= 0.5 || inner.Height <= 0.5)
        {
            if (pushedOpacity) dc.Pop();
            return;
        }

        switch (element.Kind)
        {
            case HudElementKind.Text:
            case HudElementKind.Icon:
                DrawTextElement(dc, element, data, inner, pixelsPerDip, dimUnavailable);
                break;
            case HudElementKind.Bar:
                DrawBar(dc, element, data, inner, pixelsPerDip, dimUnavailable);
                break;
            case HudElementKind.Graph:
                DrawGraph(dc, element, data, inner, pixelsPerDip);
                break;
            case HudElementKind.MetricList:
                DrawMetricList(dc, element, data, inner, pixelsPerDip, dimUnavailable);
                break;
            case HudElementKind.Divider:
                DrawDivider(dc, element, data, inner);
                break;
            case HudElementKind.Panel:
                break;
        }

        if (pushedOpacity) dc.Pop();
    }

    private static void DrawTextElement(DrawingContext dc, HudElement element, IHudDataSource data, Rect area, double ppd, bool dimUnavailable)
    {
        double? value = element.Kind == HudElementKind.Icon ? null : HudConditions.GetValue(data, element.Metric, element.CoreIndex);
        string text;

        if (element.Kind == HudElementKind.Icon)
        {
            text = string.IsNullOrEmpty(element.Glyph) ? "?" : element.Glyph;
        }
        else
        {
            string formatted = value is null
                ? MetricFormatter.Unavailable
                : MetricFormatter.Format(value.Value, element.Metric, element.Decimals, element.UnitOverride, element.UnitDivisor, element.ShowUnit);
            text = MetricFormatter.ApplyTemplate(element.Template, element.Label, formatted);
        }

        string color = HudConditions.Resolve(element, ColorTarget.Foreground, data, element.Color);
        double alpha = dimUnavailable && value is null ? 0.45 : 1.0;
        var brush = HudPainting.Brush(color, alpha);
        string family = element.Kind == HudElementKind.Icon ? element.GlyphFontFamily : element.FontFamily;

        var formattedText = HudPainting.Text(text, element.FontSize, family, element.FontWeight, element.Italic, brush, ppd, element.TextAlign);
        formattedText.MaxTextWidth = Math.Max(1, area.Width);

        double y = element.VerticalAlign switch
        {
            HudVerticalAlign.Top => area.Top,
            HudVerticalAlign.Bottom => area.Bottom - formattedText.Height,
            _ => area.Top + (area.Height - formattedText.Height) / 2,
        };
        dc.DrawText(formattedText, new Point(area.Left, y));
    }

    private static void DrawBar(DrawingContext dc, HudElement element, IHudDataSource data, Rect area, double ppd, bool dimUnavailable)
    {
        double radius = Math.Min(element.CornerRadius, Math.Min(area.Width, area.Height) / 2);
        dc.DrawRoundedRectangle(HudPainting.Brush(element.TrackColor), null, area, radius, radius);

        double? value = HudConditions.GetValue(data, element.Metric, element.CoreIndex);
        double ratio = 0;
        if (value.HasValue)
        {
            double range = element.RangeMax - element.RangeMin;
            ratio = range > 0 ? (value.Value - element.RangeMin) / range : 0;
            ratio = Math.Clamp(ratio, 0, 1);
        }

        if (ratio > 0)
        {
            string fillColor = HudConditions.Resolve(element, ColorTarget.BarFill, data, element.BarFillColor);
            double opacity = dimUnavailable && value is null ? 0.4 : 1.0;
            var brush = HudPainting.Brush(fillColor, opacity);

            Rect fill = element.BarDirection switch
            {
                BarDirection.RightToLeft => new Rect(area.Right - area.Width * ratio, area.Top, area.Width * ratio, area.Height),
                BarDirection.BottomToTop => new Rect(area.Left, area.Bottom - area.Height * ratio, area.Width, area.Height * ratio),
                BarDirection.TopToBottom => new Rect(area.Left, area.Top, area.Width, area.Height * ratio),
                _ => new Rect(area.Left, area.Top, area.Width * ratio, area.Height),
            };
            double fillRadius = Math.Min(radius, Math.Min(fill.Width, fill.Height) / 2);
            dc.DrawRoundedRectangle(brush, null, fill, fillRadius, fillRadius);
        }

        if (!element.ShowBarValue) return;

        string text = value is null
            ? MetricFormatter.Unavailable
            : MetricFormatter.Format(value.Value, element.Metric, element.Decimals, element.UnitOverride, element.UnitDivisor, element.ShowUnit);
        text = MetricFormatter.ApplyTemplate(element.Template, element.Label, text);

        string color = HudConditions.Resolve(element, ColorTarget.Foreground, data, element.Color);
        var textBrush = HudPainting.Brush(color, dimUnavailable && value is null ? 0.45 : 1.0);
        var formatted = HudPainting.Text(text, element.BarValueFontSize, element.FontFamily, element.FontWeight, false, textBrush, ppd, HudTextAlign.Right);
        formatted.MaxTextWidth = Math.Max(1, area.Width - 4);

        double y = area.Top + (area.Height - formatted.Height) / 2;
        dc.DrawText(formatted, new Point(area.Left + 2, y));
    }

    private static void DrawGraph(DrawingContext dc, HudElement element, IHudDataSource data, Rect area, double ppd)
    {
        var history = data.GetHistory(element.Metric, element.WindowSeconds);

        if (element.ShowGrid)
        {
            var gridPen = HudPainting.Pen(element.GridColor, 1, 0.8);
            for (int i = 0; i <= 3; i++)
            {
                double y = area.Top + area.Height * i / 3.0;
                dc.DrawLine(gridPen, new Point(area.Left, y), new Point(area.Right, y));
            }
        }

        if (history.Length < 2) return;

        double min = double.MaxValue, max = double.MinValue;
        foreach (var v in history)
        {
            if (float.IsNaN(v)) continue;
            if (v < min) min = v;
            if (v > max) max = v;
        }
        if (min > max) return;

        if (!element.AutoScale)
        {
            min = element.ScaleMin;
            max = element.ScaleMax;
        }
        else
        {
            double pad = (max - min) * 0.1;
            if (pad < 0.5) pad = 0.5;
            min -= pad;
            max += pad;
            if (MetricCatalog.Get(element.Metric).Unit is MetricUnit.Percent or MetricUnit.Fps && min < 0) min = 0;
        }
        if (max - min < 0.0001) max = min + 1;

        double Range(double v) => area.Bottom - (v - min) / (max - min) * area.Height;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(area.Left, area.Bottom), element.ShowGraphFill, false);
            for (int i = 0; i < history.Length; i++)
            {
                double x = area.Left + area.Width * i / (history.Length - 1.0);
                double y = Range(history[i]);
                ctx.LineTo(new Point(x, y), i > 0, false);
            }
            if (element.ShowGraphFill)
            {
                ctx.LineTo(new Point(area.Right, area.Bottom), true, false);
            }
        }
        geometry.Freeze();

        string accent = HudConditions.Resolve(element, ColorTarget.BarFill, data, element.LineColor);
        if (element.ShowGraphFill)
        {
            dc.DrawGeometry(HudPainting.Brush(accent, element.GraphFillOpacity), null, geometry);
        }

        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(new Point(area.Left, Range(history[0])), false, false);
            for (int i = 1; i < history.Length; i++)
            {
                double x = area.Left + area.Width * i / (history.Length - 1.0);
                ctx.LineTo(new Point(x, Range(history[i])), true, false);
            }
        }
        line.Freeze();
        dc.DrawGeometry(null, HudPainting.Pen(accent, element.LineWidth), line);

        if (element.ShowGraphStats)
        {
            double sum = 0, peak = double.MinValue, low = double.MaxValue;
            int count = 0;
            foreach (var v in history)
            {
                if (float.IsNaN(v)) continue;
                sum += v;
                if (v > peak) peak = v;
                if (v < low) low = v;
                count++;
            }
            if (count > 0)
            {
                string text = $"{MetricFormatter.Format(sum / count, element.Metric, 0, null, 1, false)} / {MetricFormatter.Format(peak, element.Metric, 0, null, 1, false)}";
                var brush = HudPainting.Brush(element.Color, 0.75);
                var ft = HudPainting.Text(text, element.BarValueFontSize, element.FontFamily, 500, false, brush, ppd, HudTextAlign.Right);
                ft.MaxTextWidth = Math.Max(1, area.Width - 4);
                dc.DrawText(ft, new Point(area.Left + 2, area.Top + 2));
            }
        }
    }

    private static void DrawMetricList(DrawingContext dc, HudElement element, IHudDataSource data, Rect area, double ppd, bool dimUnavailable)
    {
        int count = element.Items.Count;
        if (count == 0) return;

        double rowHeight = area.Height / count;
        double fontSize = Math.Min(element.FontSize, Math.Max(6, rowHeight - element.RowSpacing));

        for (int i = 0; i < count; i++)
        {
            var item = element.Items[i];
            double top = area.Top + i * rowHeight;
            double? value = HudConditions.GetValue(data, item.Metric, item.CoreIndex);

            string valueText = MetricFormatter.FormatValue(item.Metric, value, item.Decimals, item.UnitOverride, item.UnitDivisor, element.ShowUnit);
            string color = item.Color ?? HudConditions.Resolve(element, ColorTarget.Foreground, data, element.Color);
            double alpha = dimUnavailable && value is null ? 0.45 : 1.0;

            string? label = string.IsNullOrEmpty(item.Label) ? null : item.Label;
            if (element.ShowMetricListLabels && label is not null)
            {
                var labelBrush = HudPainting.Brush(color, alpha * 0.72);
                var labelText = HudPainting.Text(label, fontSize, element.FontFamily, 500, false, labelBrush, ppd, HudTextAlign.Left);
                labelText.MaxTextWidth = Math.Max(1, element.LabelColumnWidth);
                double ly = top + (rowHeight - labelText.Height) / 2;
                dc.DrawText(labelText, new Point(area.Left, ly));
            }

            var valueBrush = HudPainting.Brush(color, alpha);
            var valueFormatted = HudPainting.Text(valueText, fontSize, element.FontFamily, element.FontWeight, false, valueBrush, ppd, HudTextAlign.Right);
            valueFormatted.MaxTextWidth = Math.Max(1, area.Width - (element.ShowMetricListLabels ? element.LabelColumnWidth : 0));
            double vy = top + (rowHeight - valueFormatted.Height) / 2;
            double vx = area.Left + (element.ShowMetricListLabels ? element.LabelColumnWidth : 0);
            dc.DrawText(valueFormatted, new Point(vx, vy));
        }
    }

    private static void DrawDivider(DrawingContext dc, HudElement element, IHudDataSource data, Rect area)
    {
        string color = HudConditions.Resolve(element, ColorTarget.Border, data, element.BorderColor ?? element.Color);
        var pen = HudPainting.Pen(color, element.DividerThickness);
        if (element.DividerOrientation == DividerOrientation.Horizontal)
        {
            double y = area.Top + area.Height / 2;
            dc.DrawLine(pen, new Point(area.Left, y), new Point(area.Right, y));
        }
        else
        {
            double x = area.Left + area.Width / 2;
            dc.DrawLine(pen, new Point(x, area.Top), new Point(x, area.Bottom));
        }
    }
}
