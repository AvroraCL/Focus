using System.IO;
using Focus.Core.Sessions;
using Xunit;

namespace Focus.Tests;

public class SessionTests
{
    [Fact]
    public void Compute_ProducesConsistentStatistics()
    {
        var frames = new List<double>();
        for (int i = 0; i < 990; i++) frames.Add(5.0); // 200 fps
        for (int i = 0; i < 10; i++) frames.Add(100.0); // 1% stutters at 10 fps

        var stats = SessionCalculator.Compute(frames);
        Assert.Equal(1000, stats.FrameCount);
        Assert.InRange(stats.AverageFps, 150, 200);
        Assert.InRange(stats.OnePercentLow, 9.5, 10.5);
        Assert.Equal(10, stats.StutterCount);
        Assert.Equal(100, stats.MaxFrameTimeMs, 3);
    }

    [Fact]
    public void Compute_EmptySession_IsZeroed()
    {
        var stats = SessionCalculator.Compute(Array.Empty<double>());
        Assert.Equal(0, stats.FrameCount);
        Assert.Equal(0, stats.AverageFps);
    }

    [Fact]
    public void SessionFile_RoundTrips()
    {
        var session = new PerformanceSession
        {
            Name = "Test run",
            ProcessName = "game",
            ProcessId = 1234,
            StartedAt = DateTimeOffset.Now.AddMinutes(-2),
            EndedAt = DateTimeOffset.Now,
            FrameTimes = Enumerable.Range(0, 500).Select(i => 16.0 + i % 3).ToArray(),
        };
        session.Stats = SessionCalculator.Compute(session.FrameTimes);

        string path = Path.Combine(Path.GetTempPath(), "focus-test-" + Guid.NewGuid().ToString("N") + ".fss");
        try
        {
            SessionFileCodec.Save(session, path);
            var loaded = SessionFileCodec.Load(path);
            Assert.Equal(session.Name, loaded.Name);
            Assert.Equal(session.FrameTimes.Length, loaded.FrameTimes.Length);
            Assert.Equal(session.FrameTimes[42], loaded.FrameTimes[42], 3);
            Assert.Equal(session.Stats.AverageFps, loaded.Stats.AverageFps, 2);

            var summary = SessionFileCodec.LoadMetadataOnly(path);
            Assert.Equal(session.Id, summary.Id);
            Assert.Empty(summary.FrameTimes);
            Assert.Equal(session.Stats.FrameCount, summary.Stats.FrameCount);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void CsvExport_WritesHeaderAndRows()
    {
        var session = new PerformanceSession
        {
            Name = "csv",
            FrameTimes = new double[] { 16.6, 16.6, 33.2 },
        };
        string path = Path.Combine(Path.GetTempPath(), "focus-test-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            SessionFileCodec.ExportCsv(session, path);
            var lines = File.ReadAllLines(path);
            Assert.Equal(4, lines.Length);
            Assert.StartsWith("frame,time_seconds,frametime_ms,fps", lines[0]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
