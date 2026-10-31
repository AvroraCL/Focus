namespace Focus.Core.Metrics;

/// <summary>
/// Rolling timeline of frame present timestamps. All times are milliseconds from an arbitrary
/// monotonic origin (the capture clock).
/// </summary>
public sealed class FrameTimeline
{
    private readonly double[] _times;
    private int _start;
    private int _count;
    private long _totalFrames;

    public FrameTimeline(int capacity = 120_000)
    {
        if (capacity < 16) capacity = 16;
        _times = new double[capacity];
    }

    public long TotalFrames => _totalFrames;
    public int Count => _count;
    public int Capacity => _times.Length;

    public bool IsEmpty => _count == 0;

    public double FirstMs => _count == 0 ? 0 : At(0);
    public double LastMs => _count == 0 ? 0 : At(_count - 1);

    public double ElapsedMs => _count < 2 ? 0 : LastMs - FirstMs;

    public void Clear()
    {
        _start = 0;
        _count = 0;
        _totalFrames = 0;
    }

    public void Add(double timeMs)
    {
        if (_count > 0)
        {
            double last = At(_count - 1);
            if (timeMs <= last) return; // drop duplicated / out-of-order presents
        }

        int index = (_start + _count) % _times.Length;
        if (_count == _times.Length)
        {
            _times[_start] = timeMs;
            _start = (_start + 1) % _times.Length;
        }
        else
        {
            _times[index] = timeMs;
            _count++;
        }

        _totalFrames++;
    }

    private double At(int i) => _times[(_start + i) % _times.Length];

    /// <summary>FPS measured over the trailing window that ends at <paramref name="nowMs"/>.</summary>
    public double FpsOver(double nowMs, double windowMs = 1000.0)
    {
        if (_count < 2) return 0;
        double from = nowMs - windowMs;
        int frames = 0;
        double oldest = 0;
        for (int i = _count - 1; i >= 0; i--)
        {
            double t = At(i);
            if (t < from) break;
            frames++;
            oldest = t;
        }
        if (frames < 2) return 0;
        double span = LastMs - oldest;
        return span > 0 ? (frames - 1) * 1000.0 / span : 0;
    }

    public int CountFramesInWindow(double nowMs, double windowMs)
    {
        double from = nowMs - windowMs;
        int n = 0;
        for (int i = _count - 1; i >= 0; i--)
        {
            if (At(i) < from) break;
            n++;
        }
        return n;
    }

    /// <summary>Copies the inter-frame deltas (ms) into <paramref name="dest"/>, oldest first.</summary>
    public int CopyDeltas(Span<double> dest)
    {
        int n = Math.Min(dest.Length, Math.Max(0, _count - 1));
        int offset = _count - n;
        for (int i = 0; i < n; i++)
        {
            double a = At(offset + i);
            double b = At(offset + i + 1);
            dest[i] = b - a;
        }
        return n;
    }

    public double[] ToDeltas()
    {
        var result = new double[Math.Max(0, _count - 1)];
        CopyDeltas(result);
        return result;
    }

    public FrameStats ComputeStats(double nowMs, double instantWindowMs = 1000.0)
    {
        if (_count < 2)
        {
            return new FrameStats
            {
                FrameCount = _totalFrames,
                CurrentFps = 0,
                AverageFps = 0,
                OnePercentLow = 0,
                PointOnePercentLow = 0,
                ElapsedSeconds = 0,
            };
        }

        var deltas = ToDeltas();
        var sorted = (double[])deltas.Clone();
        Array.Sort(sorted); // ascending

        double elapsed = (LastMs - FirstMs) / 1000.0;
        double avgFps = elapsed > 0 ? (_count - 1) / elapsed : 0;

        return new FrameStats
        {
            FrameCount = _totalFrames,
            CurrentFps = FpsOver(nowMs, instantWindowMs),
            AverageFps = avgFps,
            OnePercentLow = LowFps(sorted, 0.01),
            PointOnePercentLow = LowFps(sorted, 0.001),
            MedianFrameTimeMs = Percentile(sorted, 0.50),
            P95FrameTimeMs = Percentile(sorted, 0.95),
            P99FrameTimeMs = Percentile(sorted, 0.99),
            MaxFrameTimeMs = sorted.Length > 0 ? sorted[^1] : 0,
            ElapsedSeconds = elapsed,
        };
    }

    /// <summary>
    /// Low FPS as conventionally reported by RTSS/CapFrameX: the average frame rate of the
    /// slowest <paramref name="fraction"/> of frames.
    /// </summary>
    public static double LowFps(IReadOnlyList<double> sortedAscendingDeltas, double fraction)
    {
        int n = sortedAscendingDeltas.Count;
        if (n == 0) return 0;
        int take = Math.Max(1, (int)Math.Ceiling(n * fraction));
        double sum = 0;
        for (int i = n - take; i < n; i++) sum += sortedAscendingDeltas[i];
        double mean = sum / take;
        return mean > 0 ? 1000.0 / mean : 0;
    }

    public static double Percentile(IReadOnlyList<double> sortedAscending, double p)
    {
        int n = sortedAscending.Count;
        if (n == 0) return 0;
        double pos = p * (n - 1);
        int lo = (int)Math.Floor(pos);
        int hi = (int)Math.Ceiling(pos);
        if (lo == hi) return sortedAscending[lo];
        double t = pos - lo;
        return sortedAscending[lo] * (1 - t) + sortedAscending[hi] * t;
    }
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
