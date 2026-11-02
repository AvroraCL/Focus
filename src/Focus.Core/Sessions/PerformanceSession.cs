using Focus.Core.Metrics;
using Focus.Core.Native;

namespace Focus.Core.Sessions;

public sealed class PerformanceSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string? ProcessName { get; set; }
    public int ProcessId { get; set; }
    public string? ProfileId { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset EndedAt { get; set; } = DateTimeOffset.Now;
    public SessionStats Stats { get; set; } = new();

    /// <summary>Per-frame times in milliseconds, in capture order.</summary>
    public double[] FrameTimes { get; set; } = Array.Empty<double>();

    public double DurationSeconds => Math.Max(0, (EndedAt - StartedAt).TotalSeconds);
}

public sealed class SessionStats
{
    public long FrameCount { get; set; }
    public double DurationSeconds { get; set; }
    public double AverageFps { get; set; }
    public double OnePercentLow { get; set; }
    public double PointOnePercentLow { get; set; }
    public double MedianFrameTimeMs { get; set; }
    public double P95FrameTimeMs { get; set; }
    public double P99FrameTimeMs { get; set; }
    public double MaxFrameTimeMs { get; set; }
    public int StutterCount { get; set; }
    public double StutterPercent { get; set; }
    public double? AverageGpuUsage { get; set; }
    public double? AverageCpuUsage { get; set; }
    public double? PeakGpuTemperature { get; set; }
    public double? PeakGpuPower { get; set; }
    public double? PeakCpuPower { get; set; }
}

public static class SessionCalculator
{
    /// <summary>
    /// A frame is counted as a stutter when it is slower than 2x the session median frame time,
    /// the same heuristic used by common frame analysis tools.
    /// </summary>
    public const double StutterFactor = 2.0;

    public static SessionStats Compute(IReadOnlyList<double> frameTimesMs)
    {
        var frames = frameTimesMs.ToArray();
        NativeCore.Check(NativeCore.Compute(frames, (uint)frames.Length, out var s));
        return new SessionStats
        {
            FrameCount = (long)s.Count,
            DurationSeconds = s.Seconds,
            AverageFps = s.AverageFps,
            OnePercentLow = s.LowOne,
            PointOnePercentLow = s.LowPointOne,
            MedianFrameTimeMs = s.Median,
            P95FrameTimeMs = s.P95,
            P99FrameTimeMs = s.P99,
            MaxFrameTimeMs = s.Maximum,
            StutterCount = (int)s.Stutters,
            StutterPercent = s.StutterPercent
        };
    }

    public static double[] FindSpikeIndices(IReadOnlyList<double> frameTimesMs, double factor = StutterFactor, int maxResults = 200)
    {
        if (frameTimesMs.Count == 0) return Array.Empty<double>();
        var sorted = frameTimesMs.Where(t => double.IsFinite(t) && t > 0).ToArray();
        if (sorted.Length == 0) return Array.Empty<double>();
        Array.Sort(sorted);
        double threshold = FrameTimeline.Percentile(sorted, 0.5) * factor;

        var result = new List<double>();
        for (int i = 0; i < frameTimesMs.Count && result.Count < maxResults; i++)
        {
            if (frameTimesMs[i] > threshold) result.Add(i);
        }
        return result.ToArray();
    }
}
