using Focus.Core.Metrics;
using Focus.Core.Profiles;
using Focus.Hud;
using Focus.Hud.Formatting;
using Focus.Hud.Rendering;
using Xunit;

namespace Focus.Tests;

public class FormattingTests
{
    private sealed class FakeData : IHudDataSource
    {
        public Dictionary<MetricId, double> Values { get; } = new();
        public double? Get(MetricId metric, int coreIndex = -1) => Values.TryGetValue(metric, out var v) ? v : null;
        public float[] GetHistory(MetricId metric, int windowSeconds) => Array.Empty<float>();
        public IReadOnlyList<double> CoreUsage { get; } = new double[] { 10, 90 };
        public string TargetName => "fake";
        public bool Capturing => true;
    }

    [Fact]
    public void Fps_FormatsWithoutDecimalsByDefault()
    {
        Assert.Equal("143 FPS", MetricFormatter.Format(143.4, MetricId.Fps));
        Assert.Equal("143.4 FPS", MetricFormatter.Format(143.4, MetricId.Fps, decimalsOverride: 1));
    }

    [Fact]
    public void Percent_HasNoSpace()
    {
        Assert.Equal("64%", MetricFormatter.Format(64.2, MetricId.GpuUsage));
    }

    [Fact]
    public void Memory_PromotesToGigabytes()
    {
        string text = MetricFormatter.Format(7421, MetricId.GpuMemoryUsed);
        Assert.Equal("7.2 GB", text);
        Assert.Equal("821 MB", MetricFormatter.Format(821, MetricId.GpuMemoryUsed));
    }

    [Fact]
    public void UnitOverride_IsRespected()
    {
        Assert.Equal("2.61 GHz", MetricFormatter.Format(2610, MetricId.GpuCoreClock, 2, "GHz", 1000));
    }

    [Fact]
    public void Unavailable_RendersDash()
    {
        Assert.Equal(MetricFormatter.Unavailable, MetricFormatter.FormatValue(MetricId.Fps, null));
    }

    [Fact]
    public void Template_ExpandsValueAndLabel()
    {
        Assert.Equal("GPU 64%", MetricFormatter.ApplyTemplate("GPU {value}", "GPU", "64%"));
        Assert.Equal("64% · load", MetricFormatter.ApplyTemplate("{value} · {label}", "load", "64%"));
    }

    [Fact]
    public void Conditions_PickFirstMatchingRule()
    {
        var data = new FakeData();
        data.Values[MetricId.GpuTemperature] = 85;
        var element = new HudElement
        {
            Metric = MetricId.GpuTemperature,
            Color = "#FFFFFFFF",
            Conditions =
            {
                new ConditionalColorRule
                {
                    Operator = ComparisonOperator.GreaterOrEqual,
                    Threshold = 80,
                    Target = ColorTarget.Foreground,
                    Color = "#FFFF0000",
                },
                new ConditionalColorRule
                {
                    Operator = ComparisonOperator.GreaterOrEqual,
                    Threshold = 70,
                    Target = ColorTarget.Foreground,
                    Color = "#FFFFFF00",
                },
            },
        };

        Assert.Equal("#FFFF0000", HudConditions.Resolve(element, ColorTarget.Foreground, data, element.Color));
    }

    [Fact]
    public void Conditions_FallBackWhenBelowThreshold()
    {
        var data = new FakeData();
        data.Values[MetricId.GpuTemperature] = 55;
        var element = new HudElement
        {
            Metric = MetricId.GpuTemperature,
            Color = "#FFFFFFFF",
            Conditions =
            {
                new ConditionalColorRule
                {
                    Operator = ComparisonOperator.GreaterOrEqual,
                    Threshold = 80,
                    Target = ColorTarget.Foreground,
                    Color = "#FFFF0000",
                },
            },
        };

        Assert.Equal("#FFFFFFFF", HudConditions.Resolve(element, ColorTarget.Foreground, data, element.Color));
    }

    [Fact]
    public void CoreIndex_ResolvesPerCoreValue()
    {
        var data = new FakeData();
        Assert.Equal(90, HudConditions.GetValue(data, MetricId.CpuCoreUsage, 1));
        Assert.Null(HudConditions.GetValue(data, MetricId.CpuCoreUsage, 5));
    }
}
