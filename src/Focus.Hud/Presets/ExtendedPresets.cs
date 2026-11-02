using Focus.Core.Metrics;
using Focus.Core.Profiles;

namespace Focus.Hud.Presets;

public static partial class PresetLibrary
{
    public const string SwissId = "preset-swiss";
    public const string CompetitiveId = "preset-competitive";
    public const string HardwareId = "preset-hardware";
    public const string ThermalId = "preset-thermal";
    public const string BenchmarkId = "preset-benchmark";
    public const string RibbonId = "preset-ribbon";

    private const string Paper = "#FFF5F4EF";
    private const string Ink = "#FF181818";
    private const string Red = "#FFDB3024";
    private const string SoftWhite = "#FFAFB5BA";
    private const string Cyan = "#FF74DCE8";
    private const string Lime = "#FFBCF477";
    private const string Amber = "#FFFFC477";
    private const string Violet = "#FFD1B1FF";

    public static HudProfile Swiss()
    {
        var p = NewProfile(SwissId, "Swiss Signal", 200, 112);
        p.Description = "Paper, ink and a red focus pixel. FPS and frame time in a compact corner tile.";
        p.Elements.Add(Block("Paper", 0, 0, 200, 112, "#F2F5F4EF"));
        p.Elements.Add(Block("Focus pixel", 16, 14, 8, 8, Red));
        p.Elements.Add(Label("FOCUS / FPS", 32, 10, 152, 18, 10, Ink));
        p.Elements.Add(Value("FPS", MetricId.Fps, 14, 31, 172, 52, 44, Ink, false));
        p.Elements.Add(Rule(16, 84, 168, "#40181818"));
        p.Elements.Add(Value("Frame time", MetricId.FrameTime, 16, 90, 168, 16, 11, Ink, template: "FRAME {value}", decimals: 2));
        return p;
    }

    public static HudProfile Competitive()
    {
        var p = DarkProfile(CompetitiveId, "Competitive Strip", 660, 88, HudAnchor.TopCenter);
        p.Description = "FPS, both low-percentile readings and a 30-second frame-time trace across the top edge.";
        p.Elements.Add(Block("Accent", 0, 0, 4, 88, Lime));
        p.Elements.Add(Label("FPS", 16, 9, 112, 16, 10, Lime));
        var fps = Value("FPS", MetricId.Fps, 16, 29, 112, 48, 40, Paper, false);
        fps.Conditions.Add(WarningBelow(60));
        p.Elements.Add(fps);
        Stat(p, "1% LOW", MetricId.FpsOnePercentLow, 156, 20, 96, Lime);
        Stat(p, "0.1% LOW", MetricId.FpsPointOnePercentLow, 272, 20, 100, Lime);
        Stat(p, "FRAME / ms", MetricId.FrameTime, 390, 20, 100, Lime, 2);
        p.Elements.Add(Label("FRAME TIME / 30s", 512, 9, 132, 16, 9, SoftWhite));
        p.Elements.Add(Trace(MetricId.FrameTime, 512, 32, 132, 42, Lime));
        return p;
    }

    public static HudProfile Hardware()
    {
        var p = DarkProfile(HardwareId, "Hardware Rail", 264, 350, HudAnchor.TopRight);
        p.Description = "A vertical GPU / CPU / memory dashboard for a spare screen edge.";
        p.Elements.Add(Block("Accent", 0, 0, 264, 3, Cyan));
        p.Elements.Add(Label("SYSTEM / HARDWARE", 16, 12, 232, 20, 12, Paper));
        p.Elements.Add(Label("01  GPU", 16, 48, 232, 18, 10, Cyan));
        p.Elements.Add(Readings(16, 72, 232, 88, new[]
        {
            (MetricId.GpuUsage, "Load"), (MetricId.GpuTemperature, "Temperature"),
            (MetricId.GpuPower, "Power"), (MetricId.GpuCoreClock, "Clock"),
        }));
        p.Elements.Add(Rule(16, 171, 232, Hairline));
        p.Elements.Add(Label("02  CPU", 16, 184, 232, 18, 10, Cyan));
        p.Elements.Add(Readings(16, 208, 232, 88, new[]
        {
            (MetricId.CpuUsage, "Load"), (MetricId.CpuTemperature, "Temperature"),
            (MetricId.CpuPower, "Power"), (MetricId.CpuClock, "Clock"),
        }));
        p.Elements.Add(Rule(16, 304, 232, Hairline));
        p.Elements.Add(Value("RAM", MetricId.RamUsed, 16, 318, 116, 18, 11, Paper, template: "RAM {value}", decimals: 1));
        p.Elements.Add(Value("VRAM", MetricId.GpuMemoryUsed, 140, 318, 108, 18, 11, Cyan, template: "VRAM {value}", decimals: 1));
        return p;
    }

    public static HudProfile Thermal()
    {
        var p = DarkProfile(ThermalId, "Thermal Watch", 304, 240, HudAnchor.BottomRight);
        p.Description = "GPU / CPU temperature and power, with editable temperature warning colors.";
        p.Elements.Add(Block("Accent", 0, 0, 304, 3, Amber));
        p.Elements.Add(Label("THERMALS / POWER", 16, 12, 272, 20, 12, Amber));
        ThermalRow(p, "GPU", MetricId.GpuTemperature, MetricId.GpuPower, 48, 80);
        ThermalRow(p, "CPU", MetricId.CpuTemperature, MetricId.CpuPower, 126, 85);
        p.Elements.Add(Rule(16, 202, 272, Hairline));
        var hotspot = Value("GPU hotspot", MetricId.GpuHotSpot, 16, 214, 272, 18, 11, SoftWhite, template: "GPU HOTSPOT  {value}");
        hotspot.Conditions.Add(WarningAbove(95));
        p.Elements.Add(hotspot);
        return p;
    }

    public static HudProfile Benchmark()
    {
        var p = DarkProfile(BenchmarkId, "Benchmark Desk", 536, 284, HudAnchor.BottomLeft);
        p.Description = "A detailed benchmark view with frame lows, a 30-second trace and session elapsed time.";
        p.Elements.Add(Block("Accent", 0, 0, 4, 284, Violet));
        p.Elements.Add(Label("BENCHMARK", 16, 12, 230, 18, 11, Violet));
        var session = Value("Elapsed", MetricId.SessionTime, 286, 12, 234, 18, 11, SoftWhite, template: "SESSION {value}");
        session.TextAlign = HudTextAlign.Right;
        p.Elements.Add(session);
        Stat(p, "FPS", MetricId.Fps, 16, 44, 112, Violet);
        Stat(p, "AVERAGE", MetricId.FpsAverage, 148, 44, 112, Violet, 1);
        Stat(p, "1% LOW", MetricId.FpsOnePercentLow, 280, 44, 112, Violet, 1);
        Stat(p, "0.1% LOW", MetricId.FpsPointOnePercentLow, 412, 44, 108, Violet, 1);
        p.Elements.Add(Label("FRAME TIME / 30s", 16, 107, 236, 18, 10, SoftWhite));
        var frame = Value("Frame time", MetricId.FrameTime, 286, 107, 234, 18, 11, Paper, decimals: 2);
        frame.TextAlign = HudTextAlign.Right;
        p.Elements.Add(frame);
        p.Elements.Add(Trace(MetricId.FrameTime, 16, 134, 504, 98, Violet, true));
        p.Elements.Add(Rule(16, 243, 504, Hairline));
        p.Elements.Add(Value("GPU load", MetricId.GpuUsage, 16, 255, 156, 18, 12, Paper, template: "GPU {value}"));
        p.Elements.Add(Value("CPU load", MetricId.CpuUsage, 186, 255, 156, 18, 12, Paper, template: "CPU {value}"));
        p.Elements.Add(Value("Peak frame", MetricId.FrameTimeMax, 356, 255, 164, 18, 12, Paper, template: "MAX {value}", decimals: 1));
        return p;
    }

    public static HudProfile Ribbon()
    {
        var p = DarkProfile(RibbonId, "Cinema Ribbon", 620, 44, HudAnchor.BottomCenter);
        p.Description = "A low-height bottom ribbon for FPS, frame time, system load and memory.";
        p.Elements[0].Background = "#B8181818";
        p.Elements.Add(Block("Accent", 0, 0, 3, 44, Paper));
        p.Elements.Add(Value("FPS", MetricId.Fps, 16, 9, 104, 26, 20, Paper, false, "{value} FPS"));
        p.Elements.Add(Value("Frame time", MetricId.FrameTime, 140, 12, 94, 20, 12, SoftWhite, decimals: 2));
        p.Elements.Add(Value("GPU", MetricId.GpuUsage, 250, 12, 96, 20, 12, Paper, template: "GPU {value}"));
        p.Elements.Add(Value("CPU", MetricId.CpuUsage, 362, 12, 96, 20, 12, Paper, template: "CPU {value}"));
        p.Elements.Add(Value("RAM", MetricId.RamUsed, 474, 12, 130, 20, 12, Paper, template: "RAM {value}", decimals: 1));
        return p;
    }

    private static HudProfile DarkProfile(string id, string name, double width, double height, HudAnchor anchor)
    {
        var p = NewProfile(id, name, width, height);
        p.Display.Anchor = anchor;
        p.Elements.Add(Block("Background", 0, 0, width, height, "#E0181818"));
        return p;
    }

    private static HudElement Block(string name, double x, double y, double width, double height, string color) => new()
    {
        Name = name, Kind = HudElementKind.Panel, X = x, Y = y, Width = width, Height = height,
        Background = color, PaddingLeft = 0, PaddingRight = 0, PaddingTop = 0, PaddingBottom = 0,
    };

    private static HudElement Label(string text, double x, double y, double width, double height, double size, string color) =>
        Value(text, MetricId.None, x, y, width, height, size, color, false, text);

    private static HudElement Value(string name, MetricId metric, double x, double y, double width, double height,
        double size, string color, bool unit = true, string template = "{value}", int decimals = 0) => new()
    {
        Name = name, Kind = HudElementKind.Text, Metric = metric, X = x, Y = y, Width = width, Height = height,
        FontFamily = "Arial", FontSize = size, FontWeight = size >= 20 ? 700 : 500, Color = color,
        ShowUnit = unit, Template = template, Decimals = decimals,
        PaddingLeft = 0, PaddingRight = 0, PaddingTop = 0, PaddingBottom = 0,
    };

    private static HudElement Rule(double x, double y, double width, string color) => new()
    {
        Name = "Divider", Kind = HudElementKind.Divider, X = x, Y = y, Width = width, Height = 4,
        BorderColor = color, DividerThickness = 1, PaddingLeft = 0, PaddingRight = 0, PaddingTop = 0, PaddingBottom = 0,
    };

    private static HudElement Readings(double x, double y, double width, double height, (MetricId Metric, string Label)[] items)
    {
        var list = MetricList(x, y, width, height, items);
        list.FontFamily = "Arial"; list.FontSize = 12; list.LabelColumnWidth = 100;
        list.PaddingLeft = list.PaddingRight = list.PaddingTop = list.PaddingBottom = 0;
        return list;
    }

    private static void Stat(HudProfile p, string label, MetricId metric, double x, double y, double width, string color, int decimals = 0)
    {
        p.Elements.Add(Label(label, x, y, width, 16, 10, color));
        p.Elements.Add(Value(label + " value", metric, x, y + 23, width, 32, 25, Paper, false, decimals: decimals));
    }

    private static HudElement Trace(MetricId metric, double x, double y, double width, double height, string color, bool grid = false) => new()
    {
        Name = "Frame time / 30 seconds", Kind = HudElementKind.Graph, Metric = metric,
        X = x, Y = y, Width = width, Height = height, WindowSeconds = 30,
        AutoScale = true, LineColor = color, LineWidth = 1.5, ShowGraphFill = true, GraphFillOpacity = 0.12,
        ShowGrid = grid, GridColor = "#24FFFFFF",
        PaddingLeft = 0, PaddingRight = 0, PaddingTop = 2, PaddingBottom = 2,
    };

    private static void ThermalRow(HudProfile p, string label, MetricId temperature, MetricId power, double y, double threshold)
    {
        p.Elements.Add(Label(label, 16, y, 48, 34, 12, SoftWhite));
        var temp = Value(label + " temperature", temperature, 72, y, 130, 40, 30, Paper);
        temp.Conditions.Add(WarningAbove(threshold));
        p.Elements.Add(temp);
        p.Elements.Add(Value(label + " power", power, 212, y + 9, 76, 22, 14, Amber));
        var bar = new HudElement
        {
            Name = label + " temperature bar", Kind = HudElementKind.Bar, Metric = temperature,
            X = 16, Y = y + 50, Width = 272, Height = 5, RangeMin = 0, RangeMax = 110,
            BarFillColor = Amber, TrackColor = "#28FFFFFF", ShowBarValue = false,
            PaddingLeft = 0, PaddingRight = 0, PaddingTop = 0, PaddingBottom = 0,
        };
        bar.Conditions.Add(new ConditionalColorRule { Threshold = threshold, Target = ColorTarget.BarFill, Color = "#FFFF6B6B" });
        p.Elements.Add(bar);
    }

    private static ConditionalColorRule WarningAbove(double threshold) => new() { Threshold = threshold, Color = "#FFFF6B6B" };
    private static ConditionalColorRule WarningBelow(double threshold) => new() { Operator = ComparisonOperator.Less, Threshold = threshold, Color = "#FFFF6B6B" };
}
