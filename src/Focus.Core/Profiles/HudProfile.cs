using Focus.Core.Metrics;

namespace Focus.Core.Profiles;

public enum HudElementKind
{
    Text,
    Bar,
    Graph,
    MetricList,
    Panel,
    Divider,
    Icon,
}

public enum HudAnchor
{
    TopLeft,
    TopCenter,
    TopRight,
    MiddleLeft,
    Center,
    MiddleRight,
    BottomLeft,
    BottomCenter,
    BottomRight,
}

public enum HudTextAlign
{
    Left,
    Center,
    Right,
}

public enum HudVerticalAlign
{
    Top,
    Center,
    Bottom,
}

public enum BarDirection
{
    LeftToRight,
    RightToLeft,
    BottomToTop,
    TopToBottom,
}

public enum DividerOrientation
{
    Horizontal,
    Vertical,
}

public enum ComparisonOperator
{
    Greater,
    GreaterOrEqual,
    Less,
    LessOrEqual,
}

public enum ColorTarget
{
    Foreground,
    Background,
    Border,
    BarFill,
}

public sealed class HudProfile
{
    public const int CurrentSchema = 1;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Untitled";
    public int SchemaVersion { get; set; } = CurrentSchema;
    public string? Description { get; set; }

    /// <summary>Executable name (e.g. "eldenring.exe") this profile binds to, if any.</summary>
    public string? GameExecutable { get; set; }

    public double CanvasWidth { get; set; } = 420;
    public double CanvasHeight { get; set; } = 180;

    public HudDisplayOptions Display { get; set; } = new();
    public List<HudElement> Elements { get; set; } = new();
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    public IEnumerable<MetricId> EnumerateMetrics()
    {
        foreach (var element in Elements)
        {
            if (element.Metric != MetricId.None) yield return element.Metric;
            foreach (var item in element.Items)
                if (item.Metric != MetricId.None) yield return item.Metric;
            foreach (var rule in element.Conditions)
                if (rule.Metric != MetricId.None) yield return rule.Metric;
        }
    }

    public HudElement? FindElement(string id) => Elements.FirstOrDefault(e => e.Id == id);

    public void Normalize()
    {
        SchemaVersion = CurrentSchema;
        CanvasWidth = Clamp(CanvasWidth, 80, 4096);
        CanvasHeight = Clamp(CanvasHeight, 40, 4096);
        var seen = new HashSet<string>();
        foreach (var element in Elements)
        {
            if (string.IsNullOrWhiteSpace(element.Id) || !seen.Add(element.Id))
            {
                element.Id = Guid.NewGuid().ToString("N");
                seen.Add(element.Id);
            }
            element.Normalize();
        }
    }

    public (double X, double Y, double Width, double Height) ContentBounds()
    {
        if (Elements.Count == 0) return (0, 0, 0, 0);
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var e in Elements)
        {
            minX = Math.Min(minX, e.X);
            minY = Math.Min(minY, e.Y);
            maxX = Math.Max(maxX, e.X + e.Width);
            maxY = Math.Max(maxY, e.Y + e.Height);
        }
        return (minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
    }

    public HudProfile Clone()
    {
        var copy = ProfileCodec.Parse(ProfileCodec.Serialize(this));
        copy.Normalize();
        return copy;
    }

    private static double Clamp(double value, double min, double max) => double.IsNaN(value) ? min : Math.Clamp(value, min, max);
}

public sealed class HudDisplayOptions
{
    public bool Visible { get; set; } = true;
    public double Opacity { get; set; } = 1.0;
    public double Scale { get; set; } = 1.0;

    /// <summary>Position of the HUD canvas relative to the target monitor or game window.</summary>
    public HudAnchor Anchor { get; set; } = HudAnchor.TopLeft;

    /// <summary>Pixel offset applied to the anchored position (HUD pixels, before scale).</summary>
    public double OffsetX { get; set; } = 16;

    public double OffsetY { get; set; } = 16;

    /// <summary>Logical monitor name such as \\.\DISPLAY1. Null follows the game window.</summary>
    public string? MonitorDeviceName { get; set; }

    public bool FollowGameWindow { get; set; } = true;
}

public sealed class HudElement
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public HudElementKind Kind { get; set; } = HudElementKind.Text;
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }

    public double X { get; set; } = 0;
    public double Y { get; set; } = 0;
    public double Width { get; set; } = 160;
    public double Height { get; set; } = 24;
    public int ZIndex { get; set; }
    public string? GroupId { get; set; }

    // ---- common appearance ------------------------------------------------
    public string Color { get; set; } = "#FFFFFFFF";
    public double Opacity { get; set; } = 1.0;
    public string? Background { get; set; }
    public double BackgroundOpacity { get; set; } = 1.0;
    public string? BorderColor { get; set; }
    public double BorderWidth { get; set; }
    public double CornerRadius { get; set; }
    public double PaddingLeft { get; set; } = 4;
    public double PaddingTop { get; set; } = 2;
    public double PaddingRight { get; set; } = 4;
    public double PaddingBottom { get; set; } = 2;

    // ---- metric binding ---------------------------------------------------
    public MetricId Metric { get; set; } = MetricId.None;
    public int CoreIndex { get; set; } = -1;

    // ---- text -------------------------------------------------------------
    /// <summary>Supports {value} and {label} placeholders.</summary>
    public string Template { get; set; } = "{value}";

    public string Label { get; set; } = string.Empty;
    public string FontFamily { get; set; } = "Segoe UI";
    public double FontSize { get; set; } = 14;
    public int FontWeight { get; set; } = 600;
    public bool Italic { get; set; }
    public HudTextAlign TextAlign { get; set; } = HudTextAlign.Left;
    public HudVerticalAlign VerticalAlign { get; set; } = HudVerticalAlign.Center;
    public int Decimals { get; set; } = -1;
    public bool ShowUnit { get; set; } = true;
    public string? UnitOverride { get; set; }
    public double UnitDivisor { get; set; } = 1;

    // ---- bar --------------------------------------------------------------
    public double RangeMin { get; set; }
    public double RangeMax { get; set; } = 100;
    public BarDirection BarDirection { get; set; } = BarDirection.LeftToRight;
    public string TrackColor { get; set; } = "#33FFFFFF";
    public string BarFillColor { get; set; } = "#FF4EA1FF";
    public bool ShowBarValue { get; set; } = true;
    public double BarValueFontSize { get; set; } = 11;

    // ---- graph ------------------------------------------------------------
    public int WindowSeconds { get; set; } = 30;
    public bool AutoScale { get; set; } = true;
    public double ScaleMin { get; set; }
    public double ScaleMax { get; set; } = 100;
    public bool ShowGraphFill { get; set; } = true;
    public double GraphFillOpacity { get; set; } = 0.22;
    public bool ShowGrid { get; set; }
    public string GridColor { get; set; } = "#22FFFFFF";
    public double LineWidth { get; set; } = 1.5;
    public string LineColor { get; set; } = "#FF4EA1FF";
    public bool ShowGraphStats { get; set; }

    // ---- metric list ------------------------------------------------------
    public List<MetricListItem> Items { get; set; } = new();
    public double RowSpacing { get; set; } = 4;
    public bool ShowMetricListLabels { get; set; } = true;
    public double LabelColumnWidth { get; set; } = 90;

    // ---- icon / divider ---------------------------------------------------
    public string Glyph { get; set; } = "\uE9D9";
    public string GlyphFontFamily { get; set; } = "Segoe MDL2 Assets";
    public DividerOrientation DividerOrientation { get; set; } = DividerOrientation.Horizontal;
    public double DividerThickness { get; set; } = 1;

    public List<ConditionalColorRule> Conditions { get; set; } = new();

    public void Normalize()
    {
        Width = Math.Max(4, double.IsNaN(Width) ? 4 : Width);
        Height = Math.Max(4, double.IsNaN(Height) ? 4 : Height);
        FontSize = Math.Clamp(double.IsNaN(FontSize) ? 14 : FontSize, 4, 512);
        Opacity = double.IsNaN(Opacity) ? 1 : Math.Clamp(Opacity, 0, 1);
        BackgroundOpacity = double.IsNaN(BackgroundOpacity) ? 1 : Math.Clamp(BackgroundOpacity, 0, 1);
        if (string.IsNullOrWhiteSpace(Name)) Name = Kind + " " + Id[..Math.Min(4, Id.Length)];
        foreach (var item in Items) item.Normalize();
        Conditions.RemoveAll(c => string.IsNullOrWhiteSpace(c.Color));
    }

    public IEnumerable<MetricId> EnumerateMetrics()
    {
        if (Metric != MetricId.None) yield return Metric;
        foreach (var item in Items)
            if (item.Metric != MetricId.None) yield return item.Metric;
        foreach (var rule in Conditions)
            if (rule.Metric != MetricId.None) yield return rule.Metric;
    }
}

public sealed class MetricListItem
{
    public MetricId Metric { get; set; } = MetricId.None;
    public int CoreIndex { get; set; } = -1;
    public string Label { get; set; } = string.Empty;
    public int Decimals { get; set; } = -1;
    public string? UnitOverride { get; set; }
    public double UnitDivisor { get; set; } = 1;
    public string? Color { get; set; }

    public void Normalize()
    {
        UnitDivisor = UnitDivisor <= 0 || double.IsNaN(UnitDivisor) ? 1 : UnitDivisor;
    }
}

public sealed class ConditionalColorRule
{
    public MetricId Metric { get; set; } = MetricId.None;
    public int CoreIndex { get; set; } = -1;
    public ComparisonOperator Operator { get; set; } = ComparisonOperator.GreaterOrEqual;
    public double Threshold { get; set; } = 80;
    public ColorTarget Target { get; set; } = ColorTarget.Foreground;
    public string Color { get; set; } = "#FFFF6B6B";
}
