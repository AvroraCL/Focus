using Focus.Core.Metrics;
using Focus.Core.Profiles;

namespace Focus.Hud.Presets;

/// <summary>Built-in HUD presets. Users can duplicate any of them into their own profile.</summary>
public static partial class PresetLibrary
{
    public const string MinimalId = "preset-minimal";
    public const string ClassicId = "preset-classic";
    public const string AnalysisId = "preset-analysis";

    private static readonly IReadOnlyDictionary<string, Func<HudProfile>> Factories = new Dictionary<string, Func<HudProfile>>
    {
        [MinimalId] = Minimal, [ClassicId] = Classic, [AnalysisId] = Analysis,
        [SwissId] = Swiss, [CompetitiveId] = Competitive, [HardwareId] = Hardware,
        [ThermalId] = Thermal, [BenchmarkId] = Benchmark, [RibbonId] = Ribbon,
    };

    public static IReadOnlyList<HudProfile> CreateAll() => Factories.Values.Select(create => create()).ToArray();

    public static HudProfile Create(string id) => Factories.TryGetValue(id, out var create) ? create() : Classic();

    public static bool IsPresetId(string? id) => id is not null && Factories.ContainsKey(id);

    private const string Surface = "#B8000000";
    private const string Hairline = "#1FFFFFFF";
    private const string Muted = "#8CFFFFFF";
    private const string Accent = "#FF4EA1FF";

    public static HudProfile Minimal()
    {
        var profile = NewProfile(MinimalId, "Minimal", 220, 92);
        profile.Description = "FPS and frame time only.";
        profile.Elements.Add(Panel(0, 0, 220, 92, 12));
        profile.Elements.Add(new HudElement
        {
            Kind = HudElementKind.Text,
            Name = "FPS",
            X = 16, Y = 10, Width = 96, Height = 46,
            Metric = MetricId.Fps,
            Template = "{value}",
            FontSize = 38,
            FontWeight = 700,
            ShowUnit = false,
            Decimals = 0,
            VerticalAlign = HudVerticalAlign.Center,
        });
        profile.Elements.Add(new HudElement
        {
            Kind = HudElementKind.Text,
            Name = "FPS label",
            X = 112, Y = 24, Width = 60, Height = 18,
            Template = "FPS",
            FontSize = 11,
            FontWeight = 600,
            Color = Muted,
            VerticalAlign = HudVerticalAlign.Center,
        });
        profile.Elements.Add(new HudElement
        {
            Kind = HudElementKind.Text,
            Name = "Frame time",
            X = 16, Y = 58, Width = 188, Height = 20,
            Metric = MetricId.FrameTime,
            Template = "{value}",
            FontSize = 12,
            FontWeight = 500,
            Color = Muted,
            Decimals = 2,
        });
        return profile;
    }

    public static HudProfile Classic()
    {
        var profile = NewProfile(ClassicId, "Classic", 300, 176);
        profile.Description = "Frame stats plus GPU and CPU overview.";
        profile.Elements.Add(Panel(0, 0, 300, 176, 12));
        profile.Elements.Add(new HudElement
        {
            Kind = HudElementKind.Text,
            Name = "FPS",
            X = 16, Y = 10, Width = 140, Height = 48,
            Metric = MetricId.Fps,
            Template = "{value}",
            FontSize = 40,
            FontWeight = 700,
            ShowUnit = false,
            Decimals = 0,
        });
        profile.Elements.Add(new HudElement
        {
            Kind = HudElementKind.Text,
            Name = "FPS label",
            X = 160, Y = 26, Width = 120, Height = 18,
            Template = "FPS",
            FontSize = 11,
            FontWeight = 600,
            Color = Muted,
        });
        profile.Elements.Add(new HudElement
        {
            Kind = HudElementKind.Divider,
            Name = "Divider",
            X = 16, Y = 64, Width = 268, Height = 1,
            BorderColor = Hairline,
            DividerThickness = 1,
        });
        profile.Elements.Add(MetricList(16, 74, 268, 88, new[]
        {
            (MetricId.FrameTime, "Frametime"),
            (MetricId.FpsOnePercentLow, "1% Low"),
            (MetricId.GpuUsage, "GPU"),
            (MetricId.CpuUsage, "CPU"),
        }));
        return profile;
    }

    public static HudProfile Analysis()
    {
        var profile = NewProfile(AnalysisId, "Analysis", 460, 240);
        profile.Description = "Detailed frame time graph with system load.";
        profile.Elements.Add(Panel(0, 0, 460, 240, 12));

        profile.Elements.Add(MetricList(16, 14, 196, 132, new[]
        {
            (MetricId.Fps, "FPS"),
            (MetricId.FpsAverage, "Avg"),
            (MetricId.FpsOnePercentLow, "1% Low"),
            (MetricId.FpsPointOnePercentLow, "0.1% Low"),
            (MetricId.GpuUsage, "GPU"),
            (MetricId.GpuTemperature, "GPU Temp"),
            (MetricId.CpuUsage, "CPU"),
        }));

        var graph = new HudElement
        {
            Kind = HudElementKind.Graph,
            Name = "Frame time graph",
            X = 222, Y = 14, Width = 222, Height = 132,
            Metric = MetricId.FrameTime,
            WindowSeconds = 30,
            AutoScale = false,
            ScaleMin = 0,
            ScaleMax = 33,
            ShowGrid = true,
            ShowGraphFill = true,
            LineColor = Accent,
            Background = "#1FFFFFFF",
            CornerRadius = 6,
            PaddingLeft = 4,
            PaddingTop = 4,
            PaddingRight = 4,
            PaddingBottom = 4,
        };
        graph.Conditions.Add(new ConditionalColorRule
        {
            Metric = MetricId.FrameTime,
            Operator = ComparisonOperator.GreaterOrEqual,
            Threshold = 33,
            Target = ColorTarget.BarFill,
            Color = "#FFFF6B6B",
        });
        profile.Elements.Add(graph);

        profile.Elements.Add(new HudElement
        {
            Kind = HudElementKind.Divider,
            Name = "Divider",
            X = 16, Y = 158, Width = 428, Height = 1,
            BorderColor = Hairline,
        });

        profile.Elements.Add(new HudElement
        {
            Kind = HudElementKind.Text,
            Name = "GPU load",
            X = 16, Y = 168, Width = 200, Height = 18,
            Metric = MetricId.GpuUsage,
            Template = "GPU {value}",
            FontSize = 12,
            Color = Muted,
        });
        profile.Elements.Add(new HudElement
        {
            Kind = HudElementKind.Text,
            Name = "GPU temp",
            X = 16, Y = 190, Width = 200, Height = 18,
            Metric = MetricId.GpuTemperature,
            Template = "TEMP {value}",
            FontSize = 12,
            Color = Muted,
            Conditions =
            {
                new ConditionalColorRule
                {
                    Operator = ComparisonOperator.GreaterOrEqual,
                    Threshold = 80,
                    Target = ColorTarget.Foreground,
                    Color = "#FFFF6B6B",
                },
            },
        });
        profile.Elements.Add(new HudElement
        {
            Kind = HudElementKind.Text,
            Name = "VRAM",
            X = 222, Y = 168, Width = 222, Height = 18,
            Metric = MetricId.GpuMemoryUsed,
            Template = "VRAM {value}",
            FontSize = 12,
            Color = Muted,
        });
        profile.Elements.Add(new HudElement
        {
            Kind = HudElementKind.Text,
            Name = "Session",
            X = 222, Y = 190, Width = 222, Height = 18,
            Metric = MetricId.SessionTime,
            Template = "SESSION {value}",
            FontSize = 12,
            Color = Muted,
        });
        return profile;
    }

    private static HudProfile NewProfile(string id, string name, double width, double height) => new()
    {
        Id = id,
        Name = name,
        CanvasWidth = width,
        CanvasHeight = height,
        Display = new HudDisplayOptions
        {
            Anchor = HudAnchor.TopLeft,
            OffsetX = 16,
            OffsetY = 16,
            Scale = 1,
            Opacity = 1,
        },
    };

    private static HudElement Panel(double x, double y, double w, double h, double radius) => new()
    {
        Kind = HudElementKind.Panel,
        Name = "Background",
        X = x, Y = y, Width = w, Height = h,
        Background = Surface,
        BorderColor = Hairline,
        BorderWidth = 1,
        CornerRadius = radius,
    };

    private static HudElement MetricList(double x, double y, double w, double h, (MetricId Metric, string Label)[] items)
    {
        var element = new HudElement
        {
            Kind = HudElementKind.MetricList,
            Name = "Metrics",
            X = x, Y = y, Width = w, Height = h,
            FontSize = 13,
            FontWeight = 600,
            LabelColumnWidth = 96,
            RowSpacing = 4,
            ShowMetricListLabels = true,
        };
        foreach (var (metric, label) in items)
        {
            element.Items.Add(new MetricListItem { Metric = metric, Label = label });
        }
        return element;
    }
}
