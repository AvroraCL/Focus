using Focus.Core.Native;

namespace Focus.Core.Metrics;

/// <summary>Thread-safe Rust timeline. SafeHandle owns its lifetime across native calls.</summary>
public sealed class FrameTimeline : IDisposable
{
    private readonly TimelineHandle _handle;
    private readonly object _gate = new();
    public FrameTimeline(int capacity = 120_000) => _handle = new TimelineHandle(Math.Clamp(capacity, 16, 2_000_000));
    private double[] Summary()
    {
        var result = new double[5];
        NativeCore.Check(NativeCore.Timeline(_handle, 2, 0, 0, result, 5));
        return result;
    }
    public long TotalFrames => (long)Summary()[1];
    public int Count => (int)Summary()[0];
    public int Capacity => (int)Summary()[4];
    public bool IsEmpty => Count == 0;
    public double FirstMs => Summary()[2];
    public double LastMs => Summary()[3];
    public double ElapsedMs { get { var s = Summary(); return s[3] - s[2]; } }
    public void Add(double timeMs) { lock (_gate) NativeCore.Check(NativeCore.Timeline(_handle, 0, timeMs, 0, null, 0)); }
    public void Clear() { lock (_gate) NativeCore.Check(NativeCore.Timeline(_handle, 1, 0, 0, null, 0)); }
    public int CopyDeltas(Span<double> dest)
    {
        var buffer = new double[dest.Length];
        int count = NativeCore.Timeline(_handle, 3, 0, 0, buffer, (uint)buffer.Length);
        NativeCore.Check(count); buffer.AsSpan(0, count).CopyTo(dest); return count;
    }
    public double[] ToDeltas()
    {
        lock (_gate) { var result = new double[Math.Max(0, Count - 1)]; CopyDeltas(result); return result; }
    }
    public double FpsOver(double nowMs, double windowMs = 1000) => ComputeStats(nowMs, windowMs).CurrentFps;
    public int CountFramesInWindow(double nowMs, double windowMs)
    {
        int count = NativeCore.Timeline(_handle, 4, nowMs, windowMs, null, 0); NativeCore.Check(count); return count;
    }
    public FrameStats ComputeStats(double nowMs, double instantWindowMs = 1000)
    {
        NativeCore.Check(NativeCore.TimelineStats(_handle, nowMs, instantWindowMs, out var s));
        return new FrameStats
        {
            FrameCount = (long)s.Count,
            ElapsedSeconds = s.Seconds,
            CurrentFps = s.CurrentFps,
            AverageFps = s.AverageFps,
            OnePercentLow = s.LowOne,
            PointOnePercentLow = s.LowPointOne,
            MedianFrameTimeMs = s.Median,
            P95FrameTimeMs = s.P95,
            P99FrameTimeMs = s.P99,
            MaxFrameTimeMs = s.Maximum
        };
    }
    public static double LowFps(IReadOnlyList<double> sortedAscendingDeltas, double fraction) => Quantile(sortedAscendingDeltas, fraction, 1);
    public static double Percentile(IReadOnlyList<double> sortedAscending, double p) => Quantile(sortedAscending, p, 0);
    private static double Quantile(IReadOnlyList<double> values, double fraction, uint mode)
    {
        var input = values.ToArray(); NativeCore.Check(NativeCore.Quantile(input, (uint)input.Length, fraction, mode, out var value)); return value;
    }
    public void Dispose() => _handle.Dispose();
}

public sealed class FrameStats
{
    public long FrameCount { get; init; }
    public double ElapsedSeconds { get; init; }
    public double CurrentFps { get; init; }
    public double AverageFps { get; init; }
    public double OnePercentLow { get; init; }
    public double PointOnePercentLow { get; init; }
    public double MedianFrameTimeMs { get; init; }
    public double P95FrameTimeMs { get; init; }
    public double P99FrameTimeMs { get; init; }
    public double MaxFrameTimeMs { get; init; }
}
