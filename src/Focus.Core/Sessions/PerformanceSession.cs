using Focus.Core.Metrics;

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
    public double AverageGpuUsage { get; set; }
    public double AverageCpuUsage { get; set; }
    public double PeakGpuTemperature { get; set; }
    public double PeakGpuPower { get; set; }
    public double PeakCpuPower { get; set; }
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
        var stats = new SessionStats { FrameCount = frameTimesMs.Count };
        if (frameTimesMs.Count == 0) return stats;

        var sorted = frameTimesMs.Where(t => t > 0).ToArray();
        Array.Sort(sorted);
        if (sorted.Length == 0) return stats;

        double total = 0;
        foreach (var t in sorted) total += t;
        stats.DurationSeconds = total / 1000.0;
        stats.AverageFps = stats.DurationSeconds > 0 ? sorted.Length / stats.DurationSeconds : 0;
        stats.OnePercentLow = FrameTimeline.LowFps(sorted, 0.01);
        stats.PointOnePercentLow = FrameTimeline.LowFps(sorted, 0.001);
        stats.MedianFrameTimeMs = FrameTimeline.Percentile(sorted, 0.50);
        stats.P95FrameTimeMs = FrameTimeline.Percentile(sorted, 0.95);
        stats.P99FrameTimeMs = FrameTimeline.Percentile(sorted, 0.99);
        stats.MaxFrameTimeMs = sorted[^1];

        double threshold = stats.MedianFrameTimeMs * StutterFactor;
        int stutters = 0;
        foreach (var t in sorted) if (t > threshold) stutters++;
        stats.StutterCount = stutters;
        stats.StutterPercent = sorted.Length > 0 ? stutters * 100.0 / sorted.Length : 0;
        return stats;
    }

    public static double[] FindSpikeIndices(IReadOnlyList<double> frameTimesMs, double factor = StutterFactor, int maxResults = 200)
    {
        if (frameTimesMs.Count == 0) return Array.Empty<double>();
        var sorted = frameTimesMs.Where(t => t > 0).ToArray();
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
