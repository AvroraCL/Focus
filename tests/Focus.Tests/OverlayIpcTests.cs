using Focus.Core.Ipc;
using Focus.Core.Metrics;
using System.Text;
using Xunit;

namespace Focus.Tests;

public class OverlayIpcTests
{
    [Fact]
    public void SharedChannel_RoundTripsPayload()
    {
        using var writer = SharedStateChannel.CreateWriter();
        using var reader = SharedStateChannel.OpenReader();

        byte[] payload = Encoding.UTF8.GetBytes(new string('x', 2048));
        writer.Write(payload);

        Assert.True(reader.TryReadNewest(out var received));
        Assert.Equal(payload, received);
        Assert.False(reader.TryReadNewest(out _)); // unchanged
    }

    [Fact]
    public void OverlayState_SerializesProfileAndValues()
    {
        var state = new OverlayState
        {
            TargetPid = 4242,
            TargetName = "game",
            Capturing = true,
            Visible = true,
            ProfileJson = "{\"name\":\"p\"}",
            ProfileRevision = 7,
            Values = { [MetricId.Fps.ToString()] = 144 },
            CoreUsage = new double[] { 10, 20 },
            History = { [MetricId.FrameTime.ToString()] = new float[] { 6.9f, 7.1f } },
        };

        byte[] bytes = OverlayStateCodec.Serialize(state);
        var parsed = OverlayStateCodec.Deserialize(bytes);

        Assert.NotNull(parsed);
        Assert.Equal(4242, parsed!.TargetPid);
        Assert.Equal(144, parsed.Values[MetricId.Fps.ToString()]);
        Assert.Equal(2, parsed.CoreUsage.Length);
        Assert.Equal(2, parsed.History[MetricId.FrameTime.ToString()].Length);
        Assert.Equal(7, parsed.ProfileRevision);
    }
}
