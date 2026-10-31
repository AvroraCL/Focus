using Focus.Core.Metrics;

namespace Focus.Hud;

/// <summary>
/// Deterministic sample data used only for design thumbnails, so an empty system still shows what a
/// layout looks like. Never used for the in-game overlay.
/// </summary>
public sealed class SampleHudDataSource : IHudDataSource
{
    public static readonly SampleHudDataSource Instance = new();

    private static readonly Dictionary<MetricId, double> Values = new()
    {
        [MetricId.Fps] = 143,
        [MetricId.FpsAverage] = 138.4,
        [MetricId.FpsOnePercentLow] = 96.2,
        [MetricId.FpsPointOnePercentLow] = 71.5,
        [MetricId.FrameTime] = 6.99,
        [MetricId.FrameTimeMax] = 18.4,
        [MetricId.FrameCount] = 27890,
        [MetricId.SessionTime] = 194,
        [MetricId.GpuUsage] = 64,
        [MetricId.GpuD3DUsage] = 58,
        [MetricId.GpuTemperature] = 67,
        [MetricId.GpuHotSpot] = 76,
        [MetricId.GpuCoreClock] = 2610,
        [MetricId.GpuMemoryClock] = 10501,
        [MetricId.GpuMemoryUsed] = 7421,
        [MetricId.GpuMemoryTotal] = 12288,
        [MetricId.GpuMemoryPercent] = 60,
        [MetricId.GpuPower] = 182.4,
        [MetricId.CpuUsage] = 38,
        [MetricId.CpuTemperature] = 62,
        [MetricId.CpuPower] = 74.2,
        [MetricId.CpuClock] = 4380,
        [MetricId.CpuMaxCore] = 71,
        [MetricId.RamUsed] = 18432,
        [MetricId.RamTotal] = 32768,
        [MetricId.RamPercent] = 56,
        [MetricId.ProcessWorkingSet] = 5120,
    };

    private readonly Dictionary<MetricId, float[]> _history = new();

    private SampleHudDataSource()
    {
        _history[MetricId.Fps] = BuildSeries(600, 143, 9, 0.06);
        _history[MetricId.FrameTime] = BuildSeries(600, 7.0, 1.4, 0.11);
        _history[MetricId.GpuUsage] = BuildSeries(600, 64, 7, 0.05);
        _history[MetricId.CpuUsage] = BuildSeries(600, 38, 6, 0.08);
        _history[MetricId.GpuTemperature] = BuildSeries(600, 66.5, 1.2, 0.03);
        _history[MetricId.RamPercent] = BuildSeries(600, 56, 0.8, 0.02);
    }

    private static float[] BuildSeries(int count, double center, double amplitude, double phaseStep)
    {
        var data = new float[count];
        var random = new Random(1337);
        for (int i = 0; i < count; i++)
        {
            double wave = Math.Sin(i * phaseStep) * amplitude;
            double noise = (random.NextDouble() - 0.5) * amplitude * 0.4;
            data[i] = (float)Math.Max(0, center + wave + noise);
        }
        return data;
    }

    public double? Get(MetricId metric, int coreIndex = -1)
    {
        if (metric == MetricId.CpuCoreUsage && coreIndex >= 0) return 20 + coreIndex * 4 % 60;
        return Values.TryGetValue(metric, out double value) ? value : null;
    }

    public float[] GetHistory(MetricId metric, int windowSeconds)
    {
        if (!_history.TryGetValue(metric, out var series)) return Array.Empty<float>();
        int wanted = Math.Min(series.Length, Math.Max(16, windowSeconds * 20));
        return series[^wanted..];
    }

    public IReadOnlyList<double> CoreUsage => new double[] { 34, 51, 22, 64, 12, 45, 71, 28, 39, 58, 17, 44 };

    public string TargetName => "Demo";

    public bool Capturing => true;
}
