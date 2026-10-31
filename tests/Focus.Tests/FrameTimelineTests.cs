using Focus.Core.Metrics;
using Xunit;

namespace Focus.Tests;

public class FrameTimelineTests
{
    [Fact]
    public void SteadySixtyFps_ReportsSixtyFps()
    {
        var timeline = new FrameTimeline();
        double t = 0;
        for (int i = 0; i < 240; i++)
        {
            timeline.Add(t);
            t += 1000.0 / 60.0;
        }

        var stats = timeline.ComputeStats(t);
        Assert.InRange(stats.CurrentFps, 59.5, 60.5);
        Assert.InRange(stats.AverageFps, 59.5, 60.5);
        Assert.InRange(stats.OnePercentLow, 55, 61);
        Assert.Equal(240, timeline.Count);
    }

    [Fact]
    public void OutOfOrderTimestamps_AreIgnored()
    {
        var timeline = new FrameTimeline();
        timeline.Add(0);
        timeline.Add(16);
        timeline.Add(12); // duplicate / regression
        timeline.Add(32);
        Assert.Equal(3, timeline.Count);
        Assert.Equal(32, timeline.LastMs);
    }

    [Fact]
    public void FpsDecaysToZero_WhenPresentsStop()
    {
        var timeline = new FrameTimeline();
        double t = 0;
        for (int i = 0; i < 120; i++)
        {
            timeline.Add(t);
            t += 1000.0 / 60.0;
        }

        double idleReference = timeline.LastMs + 3000;
        Assert.Equal(0, timeline.FpsOver(idleReference, 1000));
    }

    [Fact]
    public void OnePercentLow_UsesSlowestFrames()
    {
        // 99 frames at 10 ms plus one 100 ms frame: the slowest 1% is that single frame.
        var deltas = new List<double>();
        for (int i = 0; i < 99; i++) deltas.Add(10);
        deltas.Add(100);
        var sorted = deltas.OrderBy(d => d).ToArray();

        double low = FrameTimeline.LowFps(sorted, 0.01);
        Assert.InRange(low, 9.9, 10.1); // 1000 / 100ms
    }

    [Fact]
    public void Percentile_Interpolates()
    {
        double[] data = { 1, 2, 3, 4 };
        Assert.Equal(1, FrameTimeline.Percentile(data, 0), 3);
        Assert.Equal(2.5, FrameTimeline.Percentile(data, 0.5), 3);
        Assert.Equal(4, FrameTimeline.Percentile(data, 1), 3);
    }

    [Fact]
    public void RingBuffer_KeepsNewestSamples()
    {
        var timeline = new FrameTimeline(16);
        for (int i = 0; i < 100; i++) timeline.Add(i);
        Assert.Equal(16, timeline.Count);
        Assert.Equal(99, timeline.LastMs);
        Assert.Equal(84, timeline.FirstMs);
    }
}
