using System.Globalization;
using Focus.Core.Metrics;

namespace Focus.Hud.Formatting;

public static class MetricFormatter
{
    public const string Unavailable = "—";

    public static string UnitSymbol(MetricUnit unit) => unit switch
    {
        MetricUnit.Fps => "FPS",
        MetricUnit.Milliseconds => "ms",
        MetricUnit.Percent => "%",
        MetricUnit.Celsius => "°C",
        MetricUnit.Megahertz => "MHz",
        MetricUnit.Watts => "W",
        MetricUnit.RevolutionsPerMinute => "RPM",
        MetricUnit.Megabytes => "MB",
        MetricUnit.Seconds => "s",
        MetricUnit.Frames => "f",
        _ => string.Empty,
    };

    public static string Format(
        double value,
        MetricId metric,
        int decimalsOverride = -1,
        string? unitOverride = null,
        double divisor = 1,
        bool showUnit = true)
    {
        var definition = MetricCatalog.Get(metric);
        int decimals = decimalsOverride >= 0 ? decimalsOverride : definition.DefaultDecimals;
        double scaled = divisor > 0 && Math.Abs(divisor - 1) > double.Epsilon ? value / divisor : value;
        string? unit = unitOverride;

        if (unit is null && definition.Unit == MetricUnit.Megabytes && Math.Abs(divisor - 1) < 0.0001 && scaled >= 1024)
        {
            scaled /= 1024;
            unit = "GB";
            if (decimalsOverride < 0) decimals = 1;
        }

        unit ??= UnitSymbol(definition.Unit);

        string number = scaled.ToString("F" + Math.Clamp(decimals, 0, 4), CultureInfo.CurrentCulture);
        if (!showUnit || string.IsNullOrEmpty(unit)) return number;
        return definition.Unit is MetricUnit.Percent ? number + unit : number + " " + unit;
    }

    public static string FormatValue(MetricId metric, double? value, int decimalsOverride = -1, string? unitOverride = null, double divisor = 1, bool showUnit = true)
    {
        if (value is null) return Unavailable;
        return Format(value.Value, metric, decimalsOverride, unitOverride, divisor, showUnit);
    }

    /// <summary>Expands {value} / {label} in an element template.</summary>
    public static string ApplyTemplate(string template, string label, string formattedValue)
    {
        if (string.IsNullOrEmpty(template)) return formattedValue;
        return template.Replace("{value}", formattedValue, StringComparison.Ordinal)
                       .Replace("{label}", label ?? string.Empty, StringComparison.Ordinal);
    }
}
