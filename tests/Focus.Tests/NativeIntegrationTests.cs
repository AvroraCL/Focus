using System.Reflection;
using System.IO;
using Focus.Core.Metrics;
using Focus.Core.Native;
using Focus.Core.Sessions;
using Focus.Telemetry;
using Focus.Telemetry.Hardware;
using LibreHardwareMonitor.Hardware;
using Xunit;

namespace Focus.Tests;

public class NativeIntegrationTests
{
    [Fact]
    public void MissingHardwareDoesNotDiluteValidSamples()
    {
        var recorder = new SessionRecorder();
        recorder.Start();
        recorder.ObserveHardware(new HardwareSnapshot { GpuUsage = 80 });
        recorder.ObserveHardware(new HardwareSnapshot());
        var session = recorder.Stop("test", null, 0, null);
        Assert.Equal(80d, session.Stats.AverageGpuUsage);
        Assert.Null(session.Stats.AverageCpuUsage);
        Assert.Null(session.Stats.PeakGpuTemperature);
    }

    [Fact]
    public void LongStallsAreRecordedAndStoppedRecorderIgnoresInput()
    {
        var recorder = new SessionRecorder();
        recorder.Start(); recorder.AddFrame(6000); recorder.AddFrame(double.NaN);
        var session = recorder.Stop("test", null, 0, null);
        Assert.Equal(new double[] { 6000 }, session.FrameTimes);
        recorder.AddFrame(10);
        Assert.Equal(0, recorder.FrameCount);
    }

    [Fact]
    public void UnknownSensorNameDoesNotBorrowAnotherMetric()
    {
        var sensor = DispatchProxy.Create<ISensor, FakeSensor>();
        var pick = typeof(HardwareMonitorService).GetMethod("Pick", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Null(pick.Invoke(null, new object[] { new[] { sensor }, SensorType.Temperature, new[] { "GPU Hot Spot" } }));
        Assert.Equal(60f, pick.Invoke(null, new object[] { new[] { sensor }, SensorType.Temperature, new[] { "GPU Core" } }));
    }

    public class FakeSensor : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            "get_Name" => "GPU Core",
            "get_SensorType" => SensorType.Temperature,
            "get_Value" => (float?)60,
            _ => throw new NotSupportedException(method?.Name)
        };
    }

    [Fact]
    public void NativeSessionStatisticsRejectNonFiniteValues()
    {
        var stats = SessionCalculator.Compute(new[] { 10d, 10d, 100d, double.NaN, double.PositiveInfinity, -1d });
        Assert.Equal(3, stats.FrameCount);
        Assert.Equal(10, stats.OnePercentLow);
        Assert.Equal(1, stats.StutterCount);
    }

    [Fact]
    public void AtomicWriterReplacesUnicodePathAndPreservesLockedFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), "focus-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "璁剧疆.json");
        try
        {
            AtomicFile.WriteAllText(path, "old");
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.Throws<IOException>(() => AtomicFile.WriteAllText(path, "new"));
            Assert.Equal("old", File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(directory));
            AtomicFile.WriteAllText(path, "new");
            Assert.Equal("new", File.ReadAllText(path));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void TimelineSupportsConcurrentSnapshotsAndReset()
    {
        using var timeline = new FrameTimeline(16);
        Parallel.Invoke(
            () => { for (int i = 0; i < 5000; i++) timeline.Add(i); },
            () => { for (int i = 0; i < 500; i++) Assert.All(timeline.ToDeltas(), x => Assert.True(x > 0)); },
            () => { for (int i = 0; i < 100; i++) timeline.Clear(); });
        timeline.Clear(); timeline.Add(0); timeline.Add(10);
        Assert.Equal(new double[] { 10 }, timeline.ToDeltas());
    }
}
