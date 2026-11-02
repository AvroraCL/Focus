using Focus.Core.Ipc;
using Focus.Core.Metrics;
using System.Text;
using Xunit;

namespace Focus.Tests;

public class OverlayIpcTests
{
    [Fact]
    public async Task ConcurrentPublicationNeverMixesPayloads()
    {
        using var writer = SharedStateChannel.CreateWriter();
        using var reader = SharedStateChannel.OpenReader();
        var publishing = Task.Run(() =>
        {
            for (int i = 1; i <= 250; i++)
                writer.Write(Enumerable.Repeat((byte)i, i * 251).ToArray());
        });
        int reads = 0;
        while (!publishing.IsCompleted)
        {
            if (reader.TryReadNewest(out var payload))
            {
                Assert.All(payload, b => Assert.Equal(payload[0], b));
                Assert.Equal(payload[0] * 251, payload.Length);
                reads++;
            }
            await Task.Delay(1);
        }
        await publishing;
        if (reader.TryReadNewest(out var last))
        {
            Assert.Equal(250 * 251, last.Length);
            Assert.All(last, b => Assert.Equal((byte)250, b));
            reads++;
        }
        Assert.True(reads > 0);
    }

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
