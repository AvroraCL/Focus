using Focus.Core.Metrics;
using Focus.Core.Profiles;
using Xunit;

namespace Focus.Tests;

public class ProfileTests
{
    [Fact]
    public void Profile_RoundTripsThroughJson()
    {
        var profile = new HudProfile { Name = "Round trip", CanvasWidth = 512, CanvasHeight = 200 };
        profile.Elements.Add(new HudElement
        {
            Kind = HudElementKind.Text,
            Name = "FPS",
            Metric = MetricId.Fps,
            Template = "{value}",
            FontSize = 24,
            TextAlign = HudTextAlign.Right,
            VerticalAlign = HudVerticalAlign.Bottom,
            Conditions =
            {
                new ConditionalColorRule
                {
                    Metric = MetricId.GpuTemperature,
                    Operator = ComparisonOperator.GreaterOrEqual,
                    Threshold = 80,
                    Target = ColorTarget.Foreground,
                    Color = "#FFFF0000",
                },
            },
        });
        profile.Elements.Add(new HudElement
        {
            Kind = HudElementKind.Bar,
            Metric = MetricId.CpuUsage,
            BarDirection = BarDirection.BottomToTop,
            Items =
            {
                new MetricListItem { Metric = MetricId.RamPercent, Label = "RAM" },
            },
        });

        string json = ProfileCodec.Serialize(profile);
        var parsed = ProfileCodec.Parse(json);

        Assert.Equal(profile.Name, parsed.Name);
        Assert.Equal(2, parsed.Elements.Count);
        var text = parsed.Elements[0];
        Assert.Equal(MetricId.Fps, text.Metric);
        Assert.Equal(HudTextAlign.Right, text.TextAlign);
        Assert.Single(text.Conditions);
        Assert.Equal(ComparisonOperator.GreaterOrEqual, text.Conditions[0].Operator);
        Assert.Equal(BarDirection.BottomToTop, parsed.Elements[1].BarDirection);
        Assert.Equal(MetricId.RamPercent, parsed.Elements[1].Items[0].Metric);
        Assert.Contains("\"fps\"", json);
    }

    [Fact]
    public void Profile_EnumeratesBoundMetrics()
    {
        var profile = new HudProfile();
        profile.Elements.Add(new HudElement { Metric = MetricId.GpuUsage });
        profile.Elements.Add(new HudElement
        {
            Metric = MetricId.Fps,
            Items = { new MetricListItem { Metric = MetricId.CpuUsage } },
            Conditions = { new ConditionalColorRule { Metric = MetricId.GpuTemperature } },
        });

        var metrics = profile.EnumerateMetrics().Distinct().ToList();
        Assert.Contains(MetricId.GpuUsage, metrics);
        Assert.Contains(MetricId.Fps, metrics);
        Assert.Contains(MetricId.CpuUsage, metrics);
        Assert.Contains(MetricId.GpuTemperature, metrics);
    }

    [Fact]
    public void Clone_IsDeep()
    {
        var profile = new HudProfile { Name = "Original" };
        var element = new HudElement { Name = "E", Width = 100 };
        profile.Elements.Add(element);

        var clone = profile.Clone();
        clone.Elements[0].Width = 400;
        clone.Elements[0].Name = "Changed";

        Assert.Equal(100, element.Width);
        Assert.Equal("E", element.Name);
    }

    [Fact]
    public void Normalize_ClampsInvalidValues()
    {
        var profile = new HudProfile { CanvasWidth = -5, CanvasHeight = 999999 };
        var element = new HudElement { Width = double.NaN, Height = 0, Opacity = 5, FontSize = -3 };
        profile.Elements.Add(element);
        profile.Normalize();

        Assert.True(profile.CanvasWidth >= 80);
        Assert.True(profile.CanvasHeight <= 4096);
        Assert.True(element.Width >= 4);
        Assert.True(element.Height >= 4);
        Assert.InRange(element.Opacity, 0, 1);
        Assert.True(element.FontSize >= 4);
    }

    [Fact]
    public void Normalize_RepairsDuplicateIds()
    {
        var profile = new HudProfile();
        profile.Elements.Add(new HudElement { Id = "same" });
        profile.Elements.Add(new HudElement { Id = "same" });
        profile.Normalize();
        Assert.NotEqual(profile.Elements[0].Id, profile.Elements[1].Id);
    }
}
