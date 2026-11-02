using Focus.Core.Metrics;
using Xunit;

namespace Focus.Tests;

public class RegressionTests
{
    [Fact]
    public void TimelinePreservesFirstSpikeAndNeverReadsBeyondEnd()
    {
        var timeline = new FrameTimeline(16);
        foreach (var t in new double[] { 100, 200, 210, 220 }) timeline.Add(t);
        Assert.Equal(new double[] { 100, 10, 10 }, timeline.ToDeltas());
        var stats = timeline.ComputeStats(220);
        Assert.Equal(10, stats.OnePercentLow);
        Assert.Equal(100, stats.MaxFrameTimeMs);
    }

    [Fact]
    public void WrappedTimelinePreservesAllIntervals()
    {
        var timeline = new FrameTimeline(16);
        for (int i = 0; i < 100; i++) timeline.Add(i);
        Assert.All(timeline.ToDeltas(), delta => Assert.Equal(1, delta));
        var latest = new double[3];
        Assert.Equal(3, timeline.CopyDeltas(latest));
        Assert.Equal(new double[] { 1, 1, 1 }, latest);
    }
}
